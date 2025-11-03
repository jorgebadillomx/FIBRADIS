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
