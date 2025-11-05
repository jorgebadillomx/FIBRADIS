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

## Observabilidad

* Cada petición recibe un `X-Request-Id` determinístico (se reutiliza si el cliente lo provee).
* Los tiempos de respuesta se agregan en histogramas simples expuestos en `/metrics`.
* CORS está abierto únicamente en ambiente de desarrollo.

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
