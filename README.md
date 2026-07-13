# InventarioWeb — Sistema de Inventario Web

Aplicación web interna para gestión de **inventario, cotizaciones y órdenes de compra**.
Construida con ASP.NET Core Razor Pages sobre .NET 10, con SQL Server como base de
datos y generación de PDFs con QuestPDF. Se ejecuta como servicio de Windows / sitio IIS
en intranet.

---

## Stack

| Componente            | Detalle                                              |
|-----------------------|------------------------------------------------------|
| Framework             | ASP.NET Core **Razor Pages**, **.NET 10**            |
| Lenguaje              | C# (`Nullable` e `ImplicitUsings` habilitados)       |
| Base de datos         | SQL Server (`Microsoft.Data.SqlClient` 6.1.4)        |
| Generación de PDF     | **QuestPDF** 2024.10.4                               |
| Hosting               | Windows Service / IIS (`Hosting.WindowsServices`)    |
| Puerto por defecto    | `http://0.0.0.0:5007`                                |

> El acceso a datos se hace con **ADO.NET crudo** (`SqlConnection` / `SqlCommand`)
> directamente en los PageModels; no se usa un ORM.

---

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (para compilar/publicar)
- Acceso a una instancia de **SQL Server** con las bases/esquemas del sistema
  (`inventario.*`, `administracion_proyectos.*`)
- Windows + IIS (solo para despliegue en el servidor)

---

## Configuración

La configuración vive en `appsettings.json` (y `appsettings.Development.json` para
desarrollo local, que **no** se versiona).

| Clave                              | Para qué sirve                                                        |
|------------------------------------|----------------------------------------------------------------------|
| `ConnectionStrings:SqlServer`      | Cadena de conexión a SQL Server. **Obligatoria.**                    |
| `Urls`                             | URL/puerto en que escucha (`http://0.0.0.0:5007`).                    |
| `AuthEnabled`                      | `true`/`false`. Activa el control de acceso por módulos (ver abajo). |
| `DemoMode`                         | Si `true`, los POST se redirigen sin persistir (modo demostración).  |
| `Banxico:Token` / `Banxico:SerieUsdMxn` | Token de la API de Banxico para el tipo de cambio USD/MXN (`SF43718`). |
| `Logging:LogLevel`                 | Nivel de logs (por defecto `Warning`).                               |

> ⚠️ No subas cadenas de conexión ni tokens reales al repositorio. Usa
> `appsettings.Development.json` (ignorado por git) o variables de entorno.

---

## Cómo correr en local

```bash
# 1. Restaurar dependencias
dotnet restore

# 2. Configurar la cadena de conexión en appsettings.Development.json
#    (ConnectionStrings:SqlServer)

# 3. Ejecutar
dotnet run
```

Luego abre `http://localhost:5007`.

**Health check:** `GET /api/health` devuelve `{ ok: true }` si la conexión a SQL Server
funciona, o `{ ok: false, error: ... }` si falla. Útil para diagnosticar despliegues.

---

## Estructura del proyecto

```
InventarioWeb/
├── Program.cs                  # Arranque: Razor Pages, sesión (8h), health check, DemoMode
├── appsettings.json            # Configuración (sin secretos)
├── Pages/                      # Páginas Razor (vista .cshtml + code-behind)
│   ├── PageBase.cs             # Clase base: sesión y VerificarAcceso(modulo)
│   ├── Index                   # Dashboard (KPIs y actividad reciente)
│   ├── Catalogo                # Catálogo de materiales
│   ├── EntradaMaterial         # Alta de entradas a inventario
│   ├── EntradaDesdeCotizacion  # Entradas generadas desde una cotización
│   ├── SalidaMaterial          # Salidas de inventario
│   ├── Devolucion              # Devoluciones de material
│   ├── Cotizaciones / NuevaCotizacion / EditarCotizacion / VerCotizacion / ExportarCotizacion
│   ├── OrdenesCompra           # Órdenes de compra (estados, cliente dinámico)
│   ├── ImprimirOC              # Generación del PDF de la orden de compra
│   ├── ImprimirSalidas         # PDF de salidas
│   ├── Login / Logout / Setup / SinAcceso   # Autenticación y control de acceso
│   └── Admin/                  # Administración de usuarios/permisos
├── Services/
│   └── AuthService.cs          # Lógica de autenticación
├── Scripts/                    # Scripts SQL de migración de esquema
├── wwwroot/                    # Estáticos (CSS, fuentes Lato para PDFs)
├── publish.ps1                 # Publica en Release y genera el ZIP
└── deploy-iis.ps1              # Despliega el ZIP en IIS (correr en el servidor)
```

### Convención de Razor Pages

Cada página tiene dos archivos: la vista `Pagina.cshtml` y su code-behind con la clase
`PaginaModel`. La mayoría de los code-behind siguen la convención `Pagina.cshtml.cs`;
algunos históricos están como `Pagina.cs` (mismo namespace y clase, solo cambia el nombre
del archivo).

---

## Control de acceso

Cuando `AuthEnabled = true`, las páginas protegidas llaman a `VerificarAcceso("modulo")`
(definido en `Pages/PageBase.cs`) al inicio de sus handlers:

- Sin sesión → redirige a `/Login`.
- Con sesión pero sin permiso del módulo → redirige a `/SinAcceso`.

El usuario se guarda en sesión (`HttpContext.Session`, expira a las 8 horas).

> **Nota para revisores:** la verificación debe llamarse en **todos** los handlers que
> leen o modifican datos (`OnGet*` y `OnPost*`), no solo en la carga de la página, ya que
> en Razor Pages cada handler se enruta de forma independiente.

---

## Publicación y despliegue

El flujo está automatizado en dos scripts PowerShell:

```powershell
# 1. En la máquina de desarrollo: compila en Release y genera InventarioWeb_v2.0.zip
powershell -ExecutionPolicy Bypass -File .\publish.ps1

# 2. Copia el ZIP al servidor y, ahí, como Administrador:
powershell -ExecutionPolicy Bypass -File .\deploy-iis.ps1
```

`deploy-iis.ps1` detiene el Application Pool, respalda la versión actual, descomprime la
nueva y reinicia IIS. **Antes de usarlo, ajusta las variables de configuración al inicio
del script** (`$appPoolName`, `$siteName`, `$sitePath`). Hay una plantilla de ejemplo en
`deploy.bat.example` para despliegue vía servicio de Windows.

> Los artefactos de compilación (`bin/`, `obj/`, `PublishOutput/`, ZIPs) están ignorados
> por git y **no** deben versionarse; se regeneran al publicar.

---

## Notas

- App pensada para **intranet** (sin HSTS/HTTPS forzado en el código).
- Integra la **API de Banxico** para el tipo de cambio USD/MXN en cotizaciones.
- Los PDFs usan la fuente **Lato** incluida en `wwwroot`.
