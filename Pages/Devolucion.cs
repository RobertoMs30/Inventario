using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class DevolucionModel : PageBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<DevolucionModel> _logger;

    public DevolucionModel(IConfiguration config, ILogger<DevolucionModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public DevolucionForm Form { get; set; } = new();

    public List<DevolucionRow> Ultimas { get; private set; } = new();
    public List<string> Proyectos { get; private set; } = new();
    public List<string> Empleados { get; private set; } = new();
    public int TotalRecords { get; private set; }
    public int NextFolio { get; private set; } = 1;

    // Filtros
    [BindProperty(SupportsGet = true)] public string? FiltCodigo   { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDesc     { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltFecha    { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltProyecto { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevuelve { get; set; }

    // ── Autocomplete búsqueda de material a partir de las últimas salidas ──
    // Se busca en inventario.salidas_inventario (misma fuente que la tabla "Últimas
    // salidas" de Salida de Material) para que Descripción, UM y Proyecto se
    // autocompleten con los datos reales de la última salida de ese código.
    public async Task<IActionResult> OnGetBuscarSalidasAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<object>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"
            WITH ranked AS (
                SELECT
                    s.cod_fab,
                    ISNULL(s.cod_int,'')             AS cod_int,
                    ISNULL(s.descripcion,'')         AS descripcion,
                    ISNULL(s.um,'')                  AS um,
                    ISNULL(s.proyecto_asignado,'')   AS proyecto,
                    ROW_NUMBER() OVER (PARTITION BY s.cod_fab ORDER BY s.id DESC) AS rn
                FROM inventario.salidas_inventario s
                WHERE s.cod_fab LIKE '%'+@q+'%' OR s.descripcion LIKE '%'+@q+'%'
            )
            SELECT TOP 10 cod_fab, cod_int, descripcion, um, proyecto
            FROM ranked
            WHERE rn = 1
            ORDER BY cod_fab;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            results.Add(new {
                codFab      = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                codInt      = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                descripcion = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                um          = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                proyecto    = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
            });
        return new JsonResult(results);
    }

    private async Task CargarEmpleadosAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var sql = @"SELECT LTRIM(RTRIM(ISNULL(Nombre,'') + ' ' + ISNULL(ApellidoPaterno,''))) AS nombre_completo
                        FROM inventario.cat_empleados
                        WHERE Nombre IS NOT NULL AND LTRIM(RTRIM(Nombre)) <> ''
                        ORDER BY ApellidoPaterno, Nombre";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var nombre = rdr.GetString(0).Trim();
                if (!string.IsNullOrEmpty(nombre)) Empleados.Add(nombre);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar la lista de empleados"); }
    }

    // ── Cargar lista de proyectos para el select ──
    private async Task CargarProyectosAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var sql = @"SELECT CAST(cotizacion AS NVARCHAR(50)) +
                               CASE WHEN ISNULL(LTRIM(RTRIM(proyecto)),'') <> ''
                                    THEN ' - ' + LTRIM(RTRIM(proyecto)) ELSE '' END AS valor
                        FROM administracion_proyectos.proyectos
                        WHERE cotizacion IS NOT NULL
                        ORDER BY cotizacion DESC";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var v = rdr.GetString(0).Trim();
                if (!string.IsNullOrEmpty(v)) Proyectos.Add(v);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar la lista de proyectos"); }
    }

    // ── Autocomplete proyecto (se mantiene por compatibilidad) ──
    public async Task<IActionResult> OnGetBuscarProyectosAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<object>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT TOP 10
                        CAST(cotizacion AS NVARCHAR(50)) AS cotizacion,
                        ISNULL(proyecto, '') AS nombre
                    FROM administracion_proyectos.proyectos
                    WHERE CAST(cotizacion AS NVARCHAR(50)) LIKE '%'+@q+'%'
                       OR ISNULL(proyecto,'') LIKE '%'+@q+'%'
                    ORDER BY cotizacion DESC;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = q.Trim();
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new {
                cotizacion = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                nombre     = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
            });
        return new JsonResult(list);
    }

    // ── Autocomplete filtros historial ──
    public async Task<IActionResult> OnGetSugCodigoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("cod_fab", q));
    }
    public async Task<IActionResult> OnGetSugDescAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("descripcion", q));
    }
    public async Task<IActionResult> OnGetSugProyectoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("proyecto", q));
    }
    public async Task<IActionResult> OnGetSugDevuelveAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("devuelve", q));
    }

    private async Task<List<string>> SugAsync(string col, string? q)
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        string where = string.IsNullOrWhiteSpace(q)
            ? $"WHERE {col} IS NOT NULL"
            : $"WHERE {col} LIKE '%'+@q+'%'";
        var sql = $"SELECT DISTINCT TOP 10 {col} FROM inventario.devoluciones_inventario {where} ORDER BY {col}";
        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(q))
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            if (!rdr.IsDBNull(0)) list.Add(rdr.GetString(0));
        return list;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        ModelState.Clear();
        if (Form.FechaDevolucion is null)
            Form.FechaDevolucion = DateTime.Today;
        await Task.WhenAll(CargarUltimasAsync(), CargarProyectosAsync(), CargarEmpleadosAsync(), CargarNextFolioAsync());
        return Page();
    }

    private async Task CargarNextFolioAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT ISNULL(MAX(id), 0) + 1 FROM inventario.devoluciones_inventario", conn);
            var result = await cmd.ExecuteScalarAsync();
            NextFolio = Convert.ToInt32(result);
        }
        catch { NextFolio = 1; }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        ModelState.Remove("Form.FechaDevolucion");
        ModelState.Remove("Form.CodFab");
        ModelState.Remove("Form.CodInt");
        ModelState.Remove("Form.Descripcion");
        ModelState.Remove("Form.Um");
        ModelState.Remove("Form.Cantidad");

        if (string.IsNullOrWhiteSpace(Form.CodFab))
            ModelState.AddModelError("", "Código es obligatorio.");
        if (string.IsNullOrWhiteSpace(Form.Descripcion))
            ModelState.AddModelError("", "Descripción es obligatoria.");
        if (Form.Cantidad is null || Form.Cantidad <= 0)
            ModelState.AddModelError("", "Cantidad debe ser mayor a 0.");
        if (Form.FechaDevolucion is null)
            ModelState.AddModelError("", "Fecha es obligatoria.");

        if (!ModelState.IsValid)
        {
            await Task.WhenAll(CargarUltimasAsync(), CargarProyectosAsync(), CargarEmpleadosAsync(), CargarNextFolioAsync());
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_RegistrarDevolucion", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@cod_fab",          Form.CodFab!.Trim());
        cmd.Parameters.AddWithValue("@cod_int",          (object?)NullIfEmpty(Form.CodInt) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@descripcion",      Form.Descripcion!.Trim());
        cmd.Parameters.AddWithValue("@cantidad",         Form.Cantidad!.Value);
        cmd.Parameters.AddWithValue("@um",               (object?)NullIfEmpty(Form.Um) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fecha_devolucion", Form.FechaDevolucion!.Value.Date);
        cmd.Parameters.AddWithValue("@motivo",           (object?)NullIfEmpty(Form.Motivo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@devuelve",         (object?)NullIfEmpty(Form.Devuelve) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@proyecto",         (object?)NullIfEmpty(Form.Proyecto) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obs",              (object?)NullIfEmpty(Form.Obs) ?? DBNull.Value);

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", $"Error al guardar: {ex.Message}");
            await Task.WhenAll(CargarUltimasAsync(), CargarNextFolioAsync());
            return Page();
        }

        return RedirectToPage("/Devolucion", new { saved = 1 });
    }

    private async Task CargarUltimasAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(FiltCodigo))   clauses.Add("cod_fab     LIKE '%'+@fCod+'%'");
        if (!string.IsNullOrEmpty(FiltDesc))     clauses.Add("descripcion LIKE '%'+@fDesc+'%'");
        if (!string.IsNullOrEmpty(FiltFecha))    clauses.Add("CONVERT(date, fecha_devolucion) = CAST(@fFecha AS date)");
        if (!string.IsNullOrEmpty(FiltProyecto)) clauses.Add("proyecto    LIKE '%'+@fProy+'%'");
        if (!string.IsNullOrEmpty(FiltDevuelve)) clauses.Add("devuelve    LIKE '%'+@fDev+'%'");
        string wc = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        var sql = $@"
SELECT id, cod_fab, descripcion, cantidad, um, fecha_devolucion, motivo, devuelve, proyecto, obs
FROM inventario.devoluciones_inventario
{wc}
ORDER BY id DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(FiltCodigo))   cmd.Parameters.Add("@fCod",   SqlDbType.NVarChar, 300).Value = FiltCodigo.Trim();
        if (!string.IsNullOrEmpty(FiltDesc))     cmd.Parameters.Add("@fDesc",  SqlDbType.NVarChar, 300).Value = FiltDesc.Trim();
        if (!string.IsNullOrEmpty(FiltFecha))    cmd.Parameters.Add("@fFecha", SqlDbType.NVarChar, 20).Value  = FiltFecha.Trim();
        if (!string.IsNullOrEmpty(FiltProyecto)) cmd.Parameters.Add("@fProy",  SqlDbType.NVarChar, 300).Value = FiltProyecto.Trim();
        if (!string.IsNullOrEmpty(FiltDevuelve)) cmd.Parameters.Add("@fDev",   SqlDbType.NVarChar, 300).Value = FiltDevuelve.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        Ultimas.Clear();
        while (await rdr.ReadAsync())
        {
            Ultimas.Add(new DevolucionRow
            {
                Id              = rdr.GetInt32(0),
                CodFab          = rdr.IsDBNull(1)  ? "" : rdr.GetString(1),
                Descripcion     = rdr.IsDBNull(2)  ? "" : rdr.GetString(2),
                Cantidad        = rdr.GetDecimal(3),
                Um              = rdr.IsDBNull(4)  ? "" : rdr.GetString(4),
                FechaDevolucion = rdr.GetDateTime(5),
                Motivo          = rdr.IsDBNull(6)  ? null : rdr.GetString(6),
                Devuelve        = rdr.IsDBNull(7)  ? null : rdr.GetString(7),
                Proyecto        = rdr.IsDBNull(8)  ? null : rdr.GetString(8),
                Obs             = rdr.IsDBNull(9)  ? null : rdr.GetString(9),
            });
        }
        TotalRecords = Ultimas.Count;
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class DevolucionForm
    {
        public string?   CodFab          { get; set; }
        public string?   CodInt          { get; set; }
        public string?   Descripcion     { get; set; }
        public decimal?  Cantidad        { get; set; }
        public string?   Um              { get; set; }
        public DateTime? FechaDevolucion { get; set; } = DateTime.Today;
        public string?   Motivo          { get; set; }
        public string?   Devuelve        { get; set; }
        public string?   Proyecto        { get; set; }
        public string?   Obs             { get; set; }
    }

    public class DevolucionRow
    {
        public int      Id              { get; set; }
        public string   CodFab          { get; set; } = "";
        public string   Descripcion     { get; set; } = "";
        public decimal  Cantidad        { get; set; }
        public string   Um              { get; set; } = "";
        public DateTime FechaDevolucion { get; set; }
        public string?  Motivo          { get; set; }
        public string?  Devuelve        { get; set; }
        public string?  Proyecto        { get; set; }
        public string?  Obs             { get; set; }
    }
}
