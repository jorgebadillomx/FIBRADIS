# FIBRADIS

## 1. Descripción general del proyecto
**Nombre:** FIBRADIS  
**Tipo:** Plataforma de portafolios de FIBRAs (Fideicomisos de Inversión en Bienes Raíces) en México.

**Stack principal**  
- **Backend:** .NET 8 + SQL Server + Hangfire.  
- **Frontend:** SPA servida vía CDN (Vercel / Cloudflare Pages).  
- **Infraestructura:** Desplegable en VPS Windows/Linux y nube (Azure Functions / Serverless SQL).

**Objetivo:**
Proporcionar a los usuarios herramientas para registrar, analizar y monitorear portafolios de FIBRAs con datos oficiales, métricas automatizadas y resúmenes asistidos por IA, asegurando trazabilidad, cumplimiento normativo y rendimiento óptimo.

**Componentes generales**  
- **Front público:** exposición de precios en tiempo real, noticias relevantes y calendario de distribuciones.  
- **Front privado:** administración del portafolio personal, métricas de rendimiento, reportes fiscales.  
- **Admin panel:** gestión de usuarios, roles, orquestación de jobs y curaduría de datos.  
- **API .NET:** capas pública (lectura), privada (operaciones autenticadas) y administrativa.  
- **Jobs Hangfire:** orquestación de procesos (quotes, news, parse, facts, dividends, recalc).  
- **Crawlers:** ingesta de documentos oficiales (PDFs) y detección de actualizaciones.  
- **LLM summarizer:** generación de resúmenes bajo un modelo BYO key provisto por el usuario.

## 2. Arquitectura general
```
┌──────────────────────────────────────────────────┐
│ FRONTEND (SPA + CDN)                            │
│  ├── Público: precios, noticias, banner          │
│  └── Privado/Admin: portafolios, usuarios, jobs  │
└──────────────────────────────────────────────────┘
             │
             ▼
┌──────────────────────────────────────────────────┐
│ API .NET 8 (REST)                               │
│  ├── Pública (read-only)                        │
│  ├── Privada (autenticada)                      │
│  └── Admin (roles/curaduría)                    │
└──────────────────────────────────────────────────┘
             │
             ▼
┌──────────────────────────────────────────────────┐
│ BACKEND / JOBS (Hangfire)                       │
│  ├── quotes / news / reports / parse / facts    │
│  ├── summarize / dividends:pull / reconcile     │
│  ├── recalc / maintenance                       │
└──────────────────────────────────────────────────┘
             │
             ▼
┌──────────────────────────────────────────────────┐
│ SQL SERVER (Entidades lógicas)                   │
│  Users / Roles / Portfolios / Trades / Positions │
│  Securities / Distributions / Facts / Documents  │
│  Jobs / JobRuns / News / Settings / AuditLogs    │
└──────────────────────────────────────────────────┘
```

## 3. Entidades lógicas principales
- **Users / Roles / AuditLogs:** autenticación, autorización y trazabilidad de acciones sensibles.  
- **Securities:** catálogo oficial de FIBRAs, precios vigentes y metadatos regulatorios.  
- **Portfolios / Positions / Trades:** posiciones históricas y actuales, valor de mercado e inversión acumulada.  
- **Distributions:** dividendos, reembolsos y rendimientos fiscales calendarizados.  
- **Documents / DocumentFacts / FactsHistory:** reportes PDF y KPIs derivados (NAV, NOI, AFFO, LTV, etc.).  
- **Jobs / JobRuns:** control operacional y auditoría de procesos automáticos.  
- **Settings / Flags:** configuración dinámica y toggles operativos.

## 4. Módulos implementados (Prompt 1 → 5)
### Prompt 1 — Fundamentos del Proyecto (.NET Core Base)
**Objetivo:** Establecer la estructura base multicapa del backend FIBRADIS.

**Componentes creados**  
- `FIBRADIS.Api`: API principal (endpoints, middlewares, health/metrics).  
- `FIBRADIS.Application`: servicios de dominio, validaciones y puertos.  
- `FIBRADIS.Domain`: entidades y contratos comunes.  
- `FIBRADIS.Infrastructure`: adaptadores de persistencia y proveedores externos.  
- `FIBRADIS.Tests.Unit` y `FIBRADIS.Tests.Integration`: suites de pruebas automatizadas.

**Características técnicas**  
- Middleware de correlación `RequestId`.  
- Logging estructurado en formato JSON.  
- Endpoints de observabilidad `/health` y `/metrics`.  
- Cobertura de pruebas unitarias e integración iniciales.

### Prompt 2 — Parser de archivo de portafolio
**Objetivo:** Normalizar portafolios en formato `.xlsx` o `.csv` provenientes de casas de bolsa y consolidarlos.

**Interfaces y modelos**  
- `IPortfolioFileParser`.  
- Modelos `NormalizedRow` y `ValidationIssue`.

**Caminos soportados**  
- **Camino A:** encabezados genéricos `FIBRA`, `Cantidad`, `Costo Promedio`.  
- **Camino B (GBM):** columnas `Emisora`, `Títulos`, `Cto. Prom.`.

**Validaciones**  
- Ticker válido en catálogo.  
- `Qty > 0` y `AvgCost > 0`.  
- Consolidación de duplicados por ticker.  
- Límite operativo: 2 MB y 5000 filas.  
- Emisión de `issues` detalladas (warnings/errores).

**Pruebas clave**  
- Archivos CSV/XLSX, caracteres especiales y culturas decimales.  
- Manejo de cancelación y filas duplicadas.

### Prompt 3 — Servicio `PortfolioReplaceService`
**Objetivo:** Reemplazar el portafolio del usuario con nuevas filas parseadas, recalcular métricas rápidas y encolar el job de recálculo.

**Interfaces involucradas**  
- `IPortfolioReplaceService`, `IPortfolioRepository`, `ISecurityCatalog`, `IDistributionReader`, `IJobScheduler`.

**Flujo funcional**  
1. Ejecuta transacción atómica (`delete + insert`).  
2. Calcula métricas rápidas (`value`, `pnl`, `weight`, `yieldTTM`, `yieldForward`).  
3. Devuelve `UploadPortfolioResponse` (snapshot consolidado).  
4. Encola `PortfolioRecalcJob` con `reason="upload"`.  
5. Registra auditoría `portfolio.upload.replace`.

**Métricas operacionales**  
- `replace_count_total`, `replace_duration_ms_p95`, `replace_errors_total`.

### Prompt 4 — Endpoint `POST /v1/portfolio/upload`
**Objetivo:** Permitir la carga autenticada del portafolio, invocar el parser, reemplazar posiciones y devolver el snapshot consolidado.

**Contrato**  
- Ruta `/v1/portfolio/upload`.  
- Autenticación JWT (`rol >= user`).  
- `multipart/form-data` con archivo `.xlsx` o `.csv`.

**Flujo**  
1. Valida presencia, tamaño y extensión del archivo.  
2. Calcula `fileHash` y registra `RequestId`.  
3. Invoca `IPortfolioFileParser` y filtra FIBRAs válidas.  
4. Llama a `PortfolioReplaceService` para reemplazo total.  
5. Devuelve `UploadPortfolioResponse` (`imported`, `ignored`, `positions`, `metrics`).  
6. Encola `PortfolioRecalcJob`.

**Errores gestionados**  
- `400` (sin filas válidas o formato incorrecto).  
- `413` (archivo > 2 MB).  
- `415` (tipo no permitido).  
- `500` (fallo interno o transacción fallida).

**Auditoría y observabilidad**  
- Evento `portfolio.upload.replace` con `userId`, `fileHash`, `positions`.  
- Logs con `RequestId`, `FileName`, `Imported`.  
- Métricas de latencia p95 y ratio de fallos.

**Pruebas clave**  
- Carga feliz (CSV/XLSX), archivos GBM, casos sin filas válidas, formatos inválidos, duplicados y rate limit.

### Prompt 5 — Job Hangfire `PortfolioRecalcJob`
**Objetivo:** Recalcular métricas avanzadas (TWR, MWR, yields) por usuario tras eventos de portafolio o mercado.

**Configuración**
- Cola `recalc`.
- Entrada `PortfolioRecalcJobInput { UserId, Reason, RequestedAt }`.  
- Idempotencia diaria (`UserId`, `Reason`, `Date`).

**Flujo funcional**  
1. Registra `JobRunId` y contexto de ejecución.  
2. Carga posiciones, precios, distribuciones y facts recientes.  
3. Calcula métricas derivadas (`invested`, `value`, `pnl`, `yieldTTM`, `yieldForward`).  
4. Calcula TWR (producto geométrico de subperiodos) y MWR (IRR de cashflows).  
5. Persistencia en `PortfolioMetrics` y `PortfolioMetricsHistory`.  
6. Actualiza auditoría `Jobs/JobRuns` con estado, duración e intentos.  
7. Emite métricas (`jobs_recalc_total`, `jobs_recalc_failed_total`, `jobs_recalc_duration_ms_p95`).

**Resiliencia**  
- Retries exponenciales (hasta 5 intentos).  
- Dead Letter Queue con contexto (`UserId`, `Reason`, excepción, stacktrace).  
- Saltos idempotentes cuando el job ya corrió para el mismo `UserId`+`Reason`+`Date` (excepto `upload`).

**Pruebas clave**
- Unitaria: éxito, idempotencia, reintentos, fallos permanentes, cálculo TWR/MWR.
- Integración: disparo tras upload, cambios de precio, batch nocturno, observabilidad.

### Prompt 6 — Parser `PdfFactsParserService`
**Objetivo:** Transformar reportes trimestrales en PDF en un conjunto de KPIs normalizados (`NAV/CBFI`, `NOI`, `AFFO`, `LTV`, `Occupancy`, `Dividends`) con scoring de calidad y persistencia histórica.

**Componentes y contratos**
- Servicio `PdfFactsParserService` que implementa `IPdfFactsParserService`.
- Entradas `ParseFactsRequest { DocumentId, FibraTicker, Url, Hash, ParserVersion }`.
- Salida `ParsedFactsResult` con KPIs normalizados, `PeriodTag`, `Score`, `SourceUrl`.
- Dependencias: `IDocumentStorage`, `IPdfTextExtractor`, `IOcrProvider`, `IFactsRepository`, `IFactsMetricsCollector`, `IClock`, `ICorrelationIdAccessor`.

**Flujo**
1. Recupera el binario desde `DocumentStorage` y valida el `Hash`.
2. Extrae texto/tablas vía `IPdfTextExtractor`; si el PDF es imagen recurre a `IOcrProvider`.
3. Detecta el periodo (`1T2025`, `4Q2024`, etc.) mediante expresiones regulares y fecha del documento.
4. Aplica reglas deterministas para localizar los KPIs en texto/tablas y normaliza unidades (porcentajes 0–1, montos en millones cuando corresponde, redondeo a 6 decimales).
5. Calcula `Score` (cobertura de campos + confianza OCR) e idempotencia (`DocumentId`, `ParserVersion`, `Hash`).
6. Si el `Score` ≥ 70, guarda en `DocumentFacts` y marca versiones previas como `superseded`; en caso contrario envía a revisión manual.
7. Registra `FactsHistory` y expone métricas (`facts_parsed_total`, `facts_failed_total`, `facts_score_avg`, `facts_duration_ms_p95`).

**Observabilidad y auditoría**
- Logs con `RequestId`, `DocumentId`, `FibraTicker`, `PeriodTag`, `Score`, `ElapsedMs`, `FieldsFound`, `Ocr`.
- Métricas agregadas mediante `FactsMetricsCollector` (invocaciones, fallos, p95, promedio de score).
- DLQ para errores críticos y reintentos cuando el OCR falla.

**Pruebas clave**
- Unitarias: texto legible, tablas estructuradas, PDF imagen con OCR, campos incompletos (score < 70), hash inválido.
- Integración: pipeline download → parse → facts, idempotencia, fallback OCR, persistencia en `DocumentFacts` y `FactsHistory`.

### Prompt 8 — Catálogo de FIBRAs (Securities)
**Ubicación:** `FIBRADIS.Api/Controllers/SecuritiesEndpoints.cs`

**Objetivo:** Centralizar el catálogo oficial de FIBRAs mexicanas con precios vigentes, KPIs y yields; exponerlo vía API pública cacheada (`GET /v1/securities`).

**Servicios y componentes**
- `ISecuritiesRepository` / `InMemorySecuritiesRepository`: almacena entidades `SecurityEntity` con KPIs actualizables por jobs (quotes, facts, distributions).
- `ISecurityRepository`: compatibilidad para módulos previos (actualización de yields desde reconciliaciones).
- `ISecuritiesCacheService` / `InMemorySecuritiesCacheService`: cache in-memory con TTL de 60 s, hash SHA-256 y serialización consistente (`SecuritiesJson`).
- `SecuritiesMetricsCollector`: métricas Prometheus (`securities_cache_hits_total`, `securities_cache_miss_total`, `securities_latency_p95`).
- Endpoint `/v1/securities` con rate limit de 60 req/min, soporte de filtro `?ticker=`, ETag y manejo de `If-None-Match`.

**Campos expuestos (DTO `SecurityDto`)**
- `Ticker`, `Name`, `Sector`, `LastPrice`, `LastPriceDate`, `YieldTTM`, `YieldForward`, `NavPerCBFI`, `Ltv`, `Occupancy`, `UpdatedAt`.

**Flujo del endpoint**
1. Consulta el cache (`ISecuritiesCacheService`).
2. Si no existe snapshot válido, recupera del repositorio, serializa y calcula ETag (SHA-256 del payload).
3. Aplica filtro por ticker cuando procede y responde con `Cache-Control: public, max-age=60` más cabecera `ETag`.
4. Responde `304 Not Modified` cuando el cliente envía un `If-None-Match` coincidente.
5. Registra logs estructurados (`RequestId`, `Ticker`, `Cached`, `ElapsedMs`, `Count`).

**Observabilidad y cache**
- TTL fijo de 60 segundos en API (preparado para CDN).
- `SecuritiesMetricsCollector` agrega latencia p95 y contadores de hits/miss, expuestos en `/metrics` junto a las métricas existentes.
- Logs integrados al pipeline estándar con `RequestTrackingMiddleware`.

**Pruebas implementadas**
- Unitarias: cache warm/hot (`GetAll_ReturnsCachedList`), invalidación tras actualización, cambio de ETag y respuestas filtradas (ticker existente / no encontrado).
- Integración: listado completo (>10 FIBRAs), consulta por ticker, estabilidad del ETag dentro del TTL, propagación de updates desde repositorio y disponibilidad de métricas.

**Integraciones clave**
- Jobs `quotes` (`IQuotesJob`), `facts` y reconciliación de dividendos invalidan el cache al actualizar métricas.
- Módulo de portafolios reutiliza el catálogo para validar tickers y calcular yields.
- Endpoint público consumido por el frontend para banner de precios.

## 5. Observabilidad Global
- Logs estructurados con `RequestId`, `UserId`, `ElapsedMs`, `Imported`, `PositionsCount`.
- Métricas de latencia (p50/p95), contadores de jobs y ratio de errores.
- Endpoints `/health` y `/metrics`.  
- Auditoría centralizada en `AuditLogs` y `Jobs/JobRuns`.

## 6. Seguridad y Cumplimiento
- HTTPS obligatorio y política de TLS actualizada.  
- Autenticación JWT con refresh tokens de corta vida.  
- Rate limiting para API pública y privada.  
- Cumplimiento `robots.txt` para crawlers.  
- Auditoría integral de acciones sensibles.  
- Cifrado y rotación de secretos (incluida la clave BYO para el LLM).

## 7. Próximos módulos
| Nº | Módulo | Descripción |
|----|--------|-------------|
| 6 | Parser PDF → Facts | Extraer KPIs (NAV, NOI, AFFO, etc.) desde reportes oficiales. |
| 7 | Distribuciones (pull + reconcile) | Importar dividendos desde Yahoo y fuentes oficiales. |
| 8 | Catálogo de Securities | Publicar `/v1/securities` con cache y filtros. |
| 9 | Front público (banner de precios) | Mostrar precios recientes y timestamp de actualización. |
| 10 | Observabilidad avanzada | Integrar Prometheus/OpenTelemetry y alertas proactivas. |

## 8. Versionado y mantenimiento
- **Versión actual:** `v0.5` (backend central hasta jobs implementados).  
- **Última actualización:** 2025-11-04.  
- **Responsable técnico:** Equipo de Arquitectura FIBRADIS.

El presente documento sirve como fuente de verdad para desarrolladores, auditores y operadores encargados del mantenimiento de FIBRADIS.
