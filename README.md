# FIBRADIS

API minimalista para exponer información operativa del portafolio de FIBRAS.

## Requisitos

* .NET 8 SDK

## Ejecución local

```bash
dotnet restore
dotnet test
dotnet run --project FIBRADIS.Api
```

La API inicia por defecto en `http://localhost:5000`.

## Endpoints

| Método | Ruta        | Descripción |
|--------|-------------|-------------|
| GET    | `/v1/ping`  | Devuelve `pong` y refleja el `X-Request-Id` enviado por el cliente. |
| GET    | `/health`   | Reporte de salud en formato JSON. |
| GET    | `/metrics`  | Métricas en formato Prometheus (latencias, solicitudes en vuelo). |

## Observabilidad avanzada

* **Módulo**: `FIBRADIS.Api/Monitoring` + `FIBRADIS.Infrastructure/Observability`.
* **Endpoints**:
  * `GET /health` → reporte JSON con `status`, lista de `checks` y `uptime` formateado.
  * `GET /metrics` → exportador Prometheus (`OpenTelemetry.Exporter.Prometheus.AspNetCore`).
* **Componentes**:
  * Health checks con sub-estados: `sqlserver`, `hangfire`, `storage_documents`, `api_public`, `api_private` (timeouts 5 s, caché local 10 s, refresh 30 s).
  * Métricas instrumentadas con `ObservabilityMetricsRegistry` (`http_requests_total`, `http_request_duration_seconds`, `jobs_total`, `jobs_failures_total`, `jobs_duration_seconds`, `db_query_duration_seconds`, `api_cache_hits_total`, `api_cache_miss_total`, `portfolio_replacements_total`, `dividends_verified_ratio`, `facts_score_avg`, `system_uptime_seconds`).
  * Logging estructurado con Serilog JSON (consola en dev, archivo diario en prod) y enriquecedores `requestId`, `jobRunId`, `userId`, `queue`, `durationMs`, `sourceContext`.
  * Tracing con OpenTelemetry (`AspNetCore`, `HttpClient`) + export OTLP (gRPC) y Prometheus para métricas.
  * Alertas recomendadas para AlertManager (`jobs_failures_total/jobs_total > 0.1`, `http_request_duration_seconds_p95 > 1s`, `dividends_verified_ratio < 0.8`, `facts_score_avg < 70`, `system_uptime_seconds == 0`).
  * Dashboards sugeridos: **API Overview**, **Jobs Performance**, **Distributions & Yields**, **Facts Quality**, **Portfolios** (Grafana + Tempo/Loki).
  * Auditoría técnica: los eventos se enriquecen con `CorrelationLogEnricher` para poblar `SystemAudit` (servicio ↔ acción ↔ resultado) correlacionado con `RequestId/JobRunId`.
* **Dependencias**: Prometheus, Grafana, Loki/Tempo, AlertManager.
* **Estado**: ✅ Implementado y validado.

## Seguridad extendida y BYO Key Tracking

* **Ubicación**: `FIBRADIS.Api/Security` + `FIBRADIS.Application/Services/Auth`.
* **Objetivo**: Autenticación JWT (15 min) con refresh tokens rotatorios (7 días), auditoría completa y controles de cuota para API privada, LLM y panel admin.
* **Roles soportados**: `viewer`, `user`, `admin`.
* **Endpoints**:
  * `POST /auth/login` → genera `accessToken` y `refreshToken` (cookie http-only) y devuelve roles.
  * `POST /auth/refresh` → valida y rota el refresh token antes de expirar.
  * `POST /auth/logout` → revoca el refresh token activo.
* **Componentes**:
  * `AuthService` + `JwtTokenService` (HMAC-SHA256, `sub`/`role`/`iat`), `InMemoryRefreshTokenStore` (revocación y rotación) y `JwtAuthMiddleware` para establecer `ClaimsPrincipal`.
  * `MemoryRateLimiterService` + `RateLimitMiddleware` con cuotas: 300 req/h por usuario (`viewer`/`user`), 60 req/min (`admin`).
  * `AesSecretService` + `InMemoryLlmUsageTracker` para BYO Key cifrada (AES-256-GCM + PBKDF2 por usuario/proveedor) y control mensual (`byok_usage_tokens_total`).
  * `InMemoryAuditService` + `AuditMiddleware` registran acciones sensibles (`auth.*`, `portfolio.upload`, endpoints admin) con IP, resultado y metadata JSON.
  * Métricas en `ObservabilityMetricsRegistry`: `auth_logins_total`, `auth_refresh_total`, `auth_failed_total`, `rate_limit_blocked_total`, `byok_keys_active_total`, `byok_usage_tokens_total`.
* **Alertas recomendadas**:
  * `auth_failed_total > 10/min` → intento de fuerza bruta.
  * `byok_usage_tokens_total` excede cuota configurada → suspender acceso LLM.
* **Pruebas**:
  * Unitarias: login/refresh/logout, fallas de credenciales, cifrado BYO Key, rate limit y auditoría.
  * Integración: flujo login→refresh→logout, acceso protegido con JWT, `/v1/securities` autenticado.
* **Estado**: ✅ Implementado y probado.

## Pipeline de Reportes (Descubrimiento → Descarga → Parse → Facts)

* **Ubicación**:
  * Jobs: `FIBRADIS.Application/Jobs/ReportsJob.cs`, `DownloadJob.cs`, `ParseJob.cs`, `FactsJob.cs`.
  * Servicios: `FIBRADIS.Application/Services/Documents/**`.
  * Infraestructura en memoria: `FIBRADIS.Api/Infrastructure/InMemoryDocumentRepository.cs`, `InMemoryDocumentDiscoveryService.cs`, `InMemoryDocumentStorage.cs`.
* **Objetivo**: automatizar el ciclo completo para encontrar reportes oficiales de FIBRAs, descargar binarios, parsear texto/tablas, clasificar el documento y extraer KPIs normalizados para el catálogo.
* **Flujo**:
  * `reports` → respeta `robots.txt`, deduplica por URL en ventana de 30 días y encola la descarga.
  * `download` → realiza HTTP GET con límite de 20 MB, calcula hash SHA-256, almacena binario y evita duplicados por hash.
  * `parse` → extrae texto/tablas (con OCR de respaldo), clasifica tipo/ticker/periodo, persiste `DocumentText` y actualiza estado.
  * `facts` → invoca `IPdfFactsParserService`, guarda `DocumentFacts`/`FactsHistory`, actualiza `Securities` y dispara `PortfolioRecalcJob(reason="kpi")` cuando el score ≥ 70.
* **Versionado e idempotencia**:
  * Descubrimiento deduplica por URL y conserva `Provenance` (referer, crawl path, `robotsOk`).
  * Descarga mantiene versiones por hash; un hash repetido marca el documento como `superseded`.
  * Parseo controla reintentos por `(Hash, ParserVersion)` y conserva métricas (`ocrUsed`, `pages`).
  * Facts utiliza `(DocumentId, ParserVersion)` y delega el versionado a `IFactsRepository` (`RequiresReview`, `IsSuperseded`).
* **Cumplimiento**: `IRobotsPolicy` administra el respeto a `robots.txt` y cooldown por dominio; User-Agent `FIBRADISBot (+contacto)`; almacenamiento mínimo necesario (hash + metadatos).
* **Observabilidad**: contadores/histogramas específicos (`reports_discovered_total`, `download_bytes_total`, `parse_duration_seconds`, `facts_score_total`) expuestos vía `DocumentPipelineMetricsCollector` y registrados en `ObservabilityMetricsRegistry`.
* **Alertas recomendadas**: `download_duplicates_total` creciente, `parse_duration_seconds_p95` elevado, `facts_score_total` decreciente o backlog en `facts`.
* **Tests**:
  * Unitarios: descubrimiento (robots/dedupe), descarga (hash duplicado), parseo (OCR + clasificación) y facts (actualiza KPIs / recalc en cola).
  * Integración: cobertura end-to-end disponible vía servicios en memoria (`InMemoryDocumentDiscoveryService` + jobs Hangfire) para validar reprocesos y bandeja de revisión.
* **Estado**: ✅ Implementado (alineado con Prompt 6 y Prompt 10).

## Resumidor LLM y Curaduría de Noticias

* **Ubicación**:
  * Jobs: `FIBRADIS.Application/Jobs/SummarizeJob.cs`, `FIBRADIS.Application/Jobs/NewsJob.cs`
  * Servicios: `FIBRADIS.Application/Services/SummarizeService.cs`, `FIBRADIS.Application/Services/NewsIngestService.cs`, `FIBRADIS.Application/Services/NewsCuratorService.cs`
  * API pública: `FIBRADIS.Api/Controllers/NewsController.cs`
* **Pipeline**: `download → parse → facts → summarize → publish(news)` con disparo diario o bajo demanda al cargar un documento.
* **Características clave**:
  * Generación de resúmenes públicos y privados con BYO Key validada, control de cuotas (`RemainingTokenQuota`) y bitácora en `ILLMUsageTracker`.
  * Ingesta de noticias externas (RSS/API) con deduplicación por hash, clasificación heurística por ticker/sector/sentimiento y almacenamiento `pending` para curaduría.
  * Curaduría admin (`NewsCuratorService`) para aprobar, editar o descartar noticias antes de publicarlas en `/v1/news` (cache 60 s) y auditoría (`summarize.generated`, `news.curated`).
  * Seguridad BYO (Prompt 11), métricas de consumo (`summarize_jobs_total`, `summarize_tokens_used_total`, `news_ingested_total`, `news_pending_total`, `news_published_total`) y alertas por cuota >90 % o backlog >50.
* **Entidades**: `Summaries`, `News`, `FactsHistory`, `LLMUsageLogs` (registro de tokens/costo por proveedor).
* **Tests**:
  * Unitarias: resúmenes con BYO Key y registro de tokens, deduplicación de noticias, clasificación por ticker/sentimiento, curaduría admin.
  * Integración: pipeline completo (facts→summaries→news), endpoints `/v1/news` y auditoría/alertas.
* **Estado**: ✅ Implementado y probado.

## Panel Admin — Usuarios, Roles y Settings

* **Ubicación**:
  * API: `FIBRADIS.Api/Controllers/AdminController.cs`
  * Servicios: `FIBRADIS.Application/Services/Admin`
  * Frontend SPA: `frontend/admin`
* **Objetivo**: ofrecer un panel `/admin` exclusivo para rol `admin` con gestión de usuarios, auditoría completa y configuración operativa (LLM, horarios y límites de seguridad).
* **Endpoints clave** (`/v1/admin/**`, JWT + rate limit 20 req/min + auditoría automática):
  * `GET /v1/admin/users` — listado paginado, búsqueda.
  * `POST /v1/admin/users` — alta con rol y password inicial.
  * `PUT /v1/admin/users/{id}` — edición de correo/rol/estado (solo admin eleva roles).
  * `DELETE /v1/admin/users/{id}` — desactivación segura.
  * `GET /v1/admin/audit` — consulta filtrable de `AuditLogs`.
  * `GET|PUT /v1/admin/settings` — lectura/actualización de `SystemSettings`.
* **Observabilidad y alertas**:
  * Métricas en `ObservabilityMetricsRegistry`: `admin_users_total`, `admin_audit_entries_total`, `admin_settings_changes_total`.
  * `AdminMetricsRecorder` registra cambios de rol (alerta >3 en 1 h) y modo mantenimiento (log → Slack).
* **SPA Admin** (React + shadcn-like components):
  * Rutas protegidas: `/admin/users`, `/admin/audit`, `/admin/settings` con navegación lateral.
  * Componentes clave: `UsersTable`, `UserEditModal`, `AuditLogTable`, `SettingsForm` (paginación, filtros, feedback visual ✔/❌).
  * Estado compartido con React Query (`frontend/admin/src/hooks/useAdminApi.ts`).
* **Pruebas**:
  * Unitarias (`AdminServiceTests`): creación/edición, política de roles, auditoría, settings y filtros de logs.
* **Estado**: ✅ Implementado y probado.

## Front público — Banner de precios

* **Ubicación**: `frontend/public/components/BannerTicker.tsx`.
* **Framework**: React + Tailwind (SPA desplegada en CDN).
* **Objetivo**: mostrar los precios y rendimientos actualizados de las FIBRAs listadas, con polling automático cada 60 s, cache local y accesibilidad AA.
* **Integración**: consume el endpoint público `/v1/securities`.
* **Características**:
  * Poll cada 60 s y pausa automática si la pestaña está inactiva.
  * Fallback a cache local (LocalStorage) y etiqueta `🔸 Desactualizado` si los datos tienen más de 5 min.
  * Animaciones con Framer Motion, modo claro/oscuro sincronizado con la preferencia del sistema y métricas de fetch en consola (`fetch_time_ms`, `cache_hit`).
* **Performance**: tamaño total < 50 KB gzip, sin dependencias pesadas.
* **Pruebas**:
  * Unitarias (render, desactualizado, polling, pausa por visibilidad, cache local, variación de color, error de red).
  * Integración (Cypress: API real, offline, dark mode, bundle < 50 KB).
* **Estado**: ✅ Implementado y probado.

## Portafolio

### Servicio `PortfolioReplaceService`

* **Ubicación**: `FIBRADIS.Application/Services/PortfolioReplaceService.cs`.
* **Dependencias clave**: `IPortfolioFileParser`, `ISecurityCatalog`, `IDistributionReader`, `IPortfolioRepository`, `IJobScheduler`.
* **Propósito**: reemplaza atómicamente el portafolio previo del usuario por las filas normalizadas importadas desde un archivo.
* **Flujo**:
  1. Inicia una transacción y elimina las posiciones existentes mediante `DeleteUserPortfolioAsync`.
  2. Inserta las nuevas posiciones y obtiene precios, yields y distribuciones necesarias.
  3. Calcula métricas consolidadas (`value`, `pnl`, `weights`, `yieldTTM`, `yieldForward`).
  4. Devuelve un `UploadPortfolioResponse` con posiciones materializadas y métricas totales.
  5. Encola el `PortfolioRecalcJob` con `reason="upload"`.
* **Validaciones**: tickers válidos del catálogo, cantidades y costos promedio mayores a cero.
* **Resiliencia**: cualquier error revierte la transacción y emite métricas (`replace_count`, `replace_duration_ms_p95`, `replace_errors_total`).
* **Auditoría**: registra el evento `portfolio.upload.replace` con `userId`, `positions`, `fileHash` y `RequestId`.

### Endpoint `POST /v1/portfolio/upload`

* **Ubicación**: `FIBRADIS.Api/Controllers/PortfolioController.cs`.
* **Rol**: privado, requiere JWT con rol `user` o superior.
* **Entrada**: archivo `.xlsx` o `.csv` en `multipart/form-data` bajo el campo `file` (máx. 2 MB).
* **Proceso**:
  1. Genera o reutiliza el `RequestId` y calcula el `fileHash` (SHA-256) del archivo.
  2. Llama a `IPortfolioFileParser.ParseAsync` para normalizar filas e issues.
  3. Filtra las filas válidas (sólo FIBRAs) y contabiliza filas ignoradas.
  4. Invoca a `PortfolioReplaceService.ReplaceAsync` para reemplazar el portafolio y obtener el snapshot.
  5. Devuelve `UploadPortfolioResponse` con `imported`, `ignored`, `positions`, `metrics` y `requestId`.
* **Errores controlados**:
  * `400 BadRequest`: archivo ausente, formato inválido o sin filas válidas.
  * `413 PayloadTooLarge`: archivo supera los 2 MB.
  * `415 UnsupportedMediaType`: extensión distinta a `.csv`/`.xlsx`.
  * `500 InternalServerError`: error interno o falla transaccional.
* **Seguridad y límites**: rate limit de 5 cargas por hora por usuario, auditoría con `fileHash` y `userId`.
* **Observabilidad**: logs estructurados (`RequestId`, `FileName`, `UserId`, `Imported`) y métricas de latencia/errores con alertas por ratio de fallos.
* **Cobertura de pruebas**: incluye casos felices (CSV/XLSX), archivos sin filas válidas, parser con issues, reemplazo atómico, encolado de job e idempotencia por `fileHash`.

### Job Hangfire `PortfolioRecalcJob`

* **Ubicación**: `FIBRADIS.Application/Jobs/PortfolioRecalcJob.cs`.
* **Cola**: `recalc`, invocado mediante Hangfire con `AutomaticRetry` exponencial (2s-32s) hasta 5 intentos.
* **Disparadores**: cargas de portafolio (`PortfolioReplaceService`), cambios de precios, distribuciones o KPIs.
* **Entradas**: `PortfolioRecalcJobInput` (`UserId`, `Reason=upload|price|kpi|distribution`, `RequestedAt`).
* **Flujo**:
  1. Registra `JobRunId`, `UserId`, `Reason` y fecha (`ExecutionDate`) en tabla de auditoría.
  2. Aplica idempotencia diaria por `(UserId, Reason)` salvo para `Reason=upload`.
  3. Carga posiciones actuales, precios (`ISecurityCatalog`), distribuciones (`IDistributionReader`), valuaciones históricas y flujos de efectivo.
  4. Calcula métricas instantáneas (`invested`, `value`, `pnl`, `yieldTTM`, `yieldForward`) y rendimientos TWR/MWR (incluye anualización cuando hay >365 días).
  5. Persiste métricas actuales e historial (`PortfolioMetricsHistory`) y marca el `JobRun` como `Success`.
  6. Emite logs estructurados y métricas (`jobs_recalc_total`, `jobs_recalc_failed_total`, `jobs_recalc_duration_ms_p95`, `jobs_recalc_positions_total`, `jobs_recalc_yield_avg`).
* **Idempotencia**: ignora ejecuciones duplicadas por día y razón (excepto `upload`).
* **Resiliencia**: retries automáticos, clasificación de errores, registro en DLQ (`PortfolioJobDeadLetterRecord`).
* **Auditoría**: inserta registros en `Jobs/JobRuns` y `DeadLetters` con `JobRunId`, `Reason`, métricas y stacktrace.
* **Cobertura de pruebas**: unitarias (casos exitosos, idempotencia, fallos transitorios, DLQ, cálculos TWR/MWR) e integrales (flujo upload→recalc, métricas persistidas y auditoría).

### Módulo `Distributions` — Yahoo + Reconciliación

* **Ubicación**:
  * Job `dividends:pull`: `FIBRADIS.Application/Jobs/DividendsPullJob.cs`.
  * Job `dividends:reconcile`: `FIBRADIS.Application/Jobs/DividendsReconcileJob.cs`.
  * Servicio de conciliación: `FIBRADIS.Application/Services/DistributionReconcilerService.cs`.
* **Objetivo**: importar distribuciones desde Yahoo Finance, reconciliarlas con fuentes oficiales (AMEFIBRA/BMV/HR) y actualizar `Distributions`, `Securities` y métricas de portafolios.
* **Flujo**:
  1. `dividends:pull` consulta tickers activos, llama a Yahoo Finance (`IDividendImporterYahoo`) y persiste eventos con estado `imported`, fuente `Yahoo` y `Confidence=0.5`.
  2. `dividends:reconcile` busca eventos `imported`, cruza con registros oficiales (`IOfficialDistributionSource`) por ticker, fecha ±7d y monto ±3%, ajusta fechas/montos/tipo, separa Dividend/CapitalReturn cuando aplica y marca como `verified` (`Confidence=0.9`).
  3. Calcula `YieldTTM` (dividendos últimos 12 meses) y `YieldForward` (último dividendo anualizado) usando precios vigentes (`ISecurityCatalog`).
  4. Actualiza `Securities`, escribe yields accesibles vía `IDistributionReader`, actualiza métricas rápidas en `PortfolioMetrics` y encola `PortfolioRecalcJob(reason="distribution")` para los usuarios con posiciones afectadas.
* **Estados de `Distributions`**: `imported`, `verified`, `ignored`, `superseded` (cuando se reemplaza por splits u oficiales). PeriodTag se recalcula con el helper `DistributionPeriodHelper` (ej. `1T2025`).
* **Observabilidad**: métricas `dividends_pull_total`, `dividends_pull_failed`, `dividends_reconcile_total`, `dividends_verified_ratio`, `yield_ttm_avg`, `yield_forward_avg` mediante `IDividendsMetricsCollector` e `IDistributionReconcileMetricsCollector`; logs estructurados con `JobRunId`, `Ticker`, `Imported`, `Verified`, `Ignored`, `ElapsedMs` y DLQ en caso de excepción.
* **Resiliencia**: reintentos exponenciales para Yahoo (hasta 3), tolerancia ±3 % en reconciliación, división de eventos mixtos, detección de datos inconsistentes (`ignored`) y fallback cuando no hay match (permanece `imported`).
* **Pruebas**:
  * Unitarias: importación Yahoo, reconciliación exacta ±3d, división dividend/capital return, tolerancia de ±3 %, casos sin match (`imported`), datos inválidos (`ignored`) y cálculo de yields.
  * Integrales: pipeline completo pull→reconcile, actualización de `Securities.YieldTTM/YieldForward`, encolado de `PortfolioRecalcJob(reason="distribution")`, auditoría/metricas y verificación de idempotencia.
