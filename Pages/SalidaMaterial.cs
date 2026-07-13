using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class SalidaMaterialModel : PageBase
{
    private readonly IConfiguration _config;

    private readonly ILogger<SalidaMaterialModel> _logger;

    public SalidaMaterialModel(IConfiguration config, ILogger<SalidaMaterialModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Formulario
    [BindProperty]
    public SalidaForm Form { get; set; } = new();

    // Últimas salidas con paginación
    public List<SalidaRow> Ultimas { get; private set; } = new();

    // Paginación
    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 20;
    public int TotalRecords { get; private set; }
    public int TotalPages => TotalRecords > 0 ? (TotalRecords + PageSize - 1) / PageSize : 1;

    // Listas para selects del formulario
    public List<string> Empleados { get; private set; } = new();
    public List<string> Proyectos { get; private set; } = new();

    // Filtros activos
    public string? FiltCodigo    { get; private set; }
    public string? FiltDesc      { get; private set; }
    public decimal? FiltCantidad { get; private set; }
    public DateTime? FiltFecha   { get; private set; }
    public string? FiltProyecto  { get; private set; }

    // ── Autocomplete filtros: buscan en salidas_inventario ───────────────────
    public async Task<IActionResult> OnGetSugCodigoAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<string>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT DISTINCT TOP 10 cod_fab FROM inventario.salidas_inventario WHERE cod_fab LIKE '%'+@q+'%' ORDER BY cod_fab;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) list.Add(rdr.IsDBNull(0) ? "" : rdr.GetString(0));
        return new JsonResult(list);
    }

    public async Task<IActionResult> OnGetSugDescAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<string>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT DISTINCT TOP 10 descripcion FROM inventario.salidas_inventario WHERE descripcion LIKE '%'+@q+'%' ORDER BY descripcion;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) list.Add(rdr.IsDBNull(0) ? "" : rdr.GetString(0));
        return new JsonResult(list);
    }

    public async Task<IActionResult> OnGetSugProyectoAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<string>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT DISTINCT TOP 10 proyecto_asignado FROM inventario.salidas_inventario WHERE proyecto_asignado LIKE '%'+@q+'%' ORDER BY proyecto_asignado;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) if (!rdr.IsDBNull(0)) list.Add(rdr.GetString(0));
        return new JsonResult(list);
    }

    // ── Autocomplete: buscar proyectos por número de cotización ──────────────
    public async Task<IActionResult> OnGetBuscarProyectosAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 10 CAST(cotizacion AS NVARCHAR(50)), ISNULL(proyecto,'')
            FROM administracion_proyectos.proyectos
            WHERE CAST(cotizacion AS NVARCHAR(50)) LIKE '%' + @q + '%'
            ORDER BY cotizacion DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 100).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            results.Add(new { num = rdr.GetString(0), nombre = rdr.GetString(1) });

        return new JsonResult(results);
    }

    // ── Autocomplete: buscar en catálogo por cod_fab / cod_int / descripción ──
    public async Task<IActionResult> OnGetBuscarCatalogoAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 10
                c.cod_fab,
                MAX(ei.cod_int)                    AS cod_int,
                ISNULL(c.no_part,   '')            AS no_part,
                ISNULL(c.descripcion, '')          AS descripcion,
                ISNULL(c.um,        '')            AS um,
                ISNULL(c.partida,   '')            AS partida,
                ISNULL(c.proveedor, '')            AS proveedor,
                ISNULL(c.pu,        0)             AS pu,
                ISNULL(c.moneda,    'MXN')         AS moneda,
                ISNULL(c.cant,      0)             AS existencia
            FROM inventario.catalogo_materiales c
            LEFT JOIN inventario.entradas_inventario ei
                   ON LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(ei.cod_fab))
            WHERE c.cod_fab     LIKE '%' + @q + '%'
               OR ei.cod_int    LIKE '%' + @q + '%'
               OR c.descripcion LIKE '%' + @q + '%'
               OR c.no_part     LIKE '%' + @q + '%'
            GROUP BY c.cod_fab, c.no_part, c.descripcion,
                     c.um, c.partida, c.proveedor, c.pu, c.moneda, c.cant
            ORDER BY c.cod_fab;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();

        while (await rdr.ReadAsync())
        {
            results.Add(new
            {
                codFab      = rdr.IsDBNull(0)  ? "" : rdr.GetString(0),
                codInt      = rdr.IsDBNull(1)  ? "" : rdr.GetString(1),
                noPart      = rdr.IsDBNull(2)  ? "" : rdr.GetString(2),
                descripcion = rdr.IsDBNull(3)  ? "" : rdr.GetString(3),
                um          = rdr.IsDBNull(4)  ? "" : rdr.GetString(4),
                partida     = rdr.IsDBNull(5)  ? "" : rdr.GetString(5),
                proveedor   = rdr.IsDBNull(6)  ? "" : rdr.GetString(6),
                pu          = rdr.IsDBNull(7)  ? 0m : rdr.GetDecimal(7),
                moneda      = rdr.IsDBNull(8)  ? "MXN" : rdr.GetString(8),
                existencia  = rdr.IsDBNull(9)  ? 0m : rdr.GetDecimal(9),
            });
        }

        return new JsonResult(results);
    }

    public async Task<IActionResult> OnGetAsync(int page = 1,
        string? filtCodigo = null, string? filtDesc = null,
        string? filtCantidad = null, string? filtFecha = null,
        string? filtProyecto = null)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        ModelState.Clear();

        if (Form.FechaSalida is null)
            Form.FechaSalida = DateTime.Today;

        FiltCodigo   = NullIfEmpty(filtCodigo);
        FiltDesc     = NullIfEmpty(filtDesc);
        FiltCantidad = decimal.TryParse(filtCantidad, out var cant) ? cant : null;
        FiltFecha    = DateTime.TryParse(filtFecha, out var fecha) ? fecha : null;
        FiltProyecto = NullIfEmpty(filtProyecto);

        if (page < 1) page = 1;
        CurrentPage = page;

        await Task.WhenAll(CargarUltimasAsync(), CargarEmpleadosAsync(), CargarProyectosAsync());

        if (string.IsNullOrWhiteSpace(Form.CodInt))
            Form.CodInt = await GenerarSiguienteCodIntAsync();

        return Page();
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

    private async Task<string> GenerarSiguienteCodIntAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT ISNULL(MAX(id), 2010) + 1 FROM inventario.salidas_inventario";
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "2011";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        // Limpiar errores automáticos del framework para campos numéricos/fecha
        ModelState.Remove("Form.Cantidad");
        ModelState.Remove("Form.FechaSalida");

        // 1) Validaciones base
        if (string.IsNullOrWhiteSpace(Form.CodFab))
            ModelState.AddModelError("", "Cod_fab es obligatorio.");

        if (string.IsNullOrWhiteSpace(Form.CodInt))
            ModelState.AddModelError("", "Cod_int es obligatorio.");

        if (string.IsNullOrWhiteSpace(Form.Descripcion))
            ModelState.AddModelError("", "Descripción es obligatoria.");

        if (Form.Cantidad is null || Form.Cantidad <= 0)
            ModelState.AddModelError("", "Cantidad debe ser mayor a 0.");

        if (string.IsNullOrWhiteSpace(Form.Um))
            ModelState.AddModelError("", "UM es obligatoria.");

        if (Form.FechaSalida is null)
            ModelState.AddModelError("", "Fecha de salida es obligatoria.");

        if (string.IsNullOrWhiteSpace(Form.Recibe))
            ModelState.AddModelError("", "Recibe es obligatorio.");

        if (string.IsNullOrWhiteSpace(Form.Instalado))
            ModelState.AddModelError("", "Instalado es obligatorio.");

        if (!ModelState.IsValid)
        {
            await CargarUltimasAsync();
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // 2) Ejecutar SP (valida stock y registra salida)
        await using var cmd = new SqlCommand("dbo.sp_RegistrarSalidaMaterial", conn);
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.AddWithValue("@cod_fab",      Form.CodFab.Trim());
        cmd.Parameters.AddWithValue("@cod_int",      Form.CodInt.Trim());
        cmd.Parameters.AddWithValue("@descripcion",  Form.Descripcion.Trim());
        cmd.Parameters.AddWithValue("@cantidad",     Form.Cantidad!.Value);
        cmd.Parameters.AddWithValue("@um",           Form.Um.Trim());
        cmd.Parameters.AddWithValue("@fecha_salida", Form.FechaSalida!.Value.Date);
        cmd.Parameters.AddWithValue("@no_salida",    (object?)NullIfEmpty(Form.NoSalida) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recibe",       Form.Recibe.Trim());
        cmd.Parameters.AddWithValue("@instalado",    Form.Instalado.Trim());
        cmd.Parameters.AddWithValue("@obs",               (object?)NullIfEmpty(Form.Obs) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@proyecto_asignado", (object?)NullIfEmpty(Form.ProyectoAsignado) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@responsable",       (object?)NullIfEmpty(Form.Responsable) ?? DBNull.Value);

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", ex.Message);
            await CargarUltimasAsync();
            return Page();
        }

        return RedirectToPage("/SalidaMaterial", new { saved = 1 });
    }

    // ── Editar una salida ya registrada ──────────────────────────────────────
    // Solo se editan campos que NO afectan el inventario (no se toca cantidad ni
    // cod_fab para no descuadrar el stock). El balance se recalcula = cantidad -
    // instalado cuando "instalado" es un número válido.
    public async Task<IActionResult> OnPostEditarAsync(
        int editId, string? editDescripcion, DateTime? editFecha, string? editNoSalida,
        string? editRecibe, string? editInstalado, string? editProyecto, string? editObs)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (editId <= 0)
            return RedirectToPage("/SalidaMaterial");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
UPDATE inventario.salidas_inventario
SET descripcion       = @desc,
    fecha_salida      = @fecha,
    no_salida         = @nosal,
    recibe            = @recibe,
    instalado         = @inst,
    proyecto_asignado = @proy,
    obs               = @obs,
    balance = CASE WHEN TRY_CAST(@inst AS DECIMAL(18,2)) IS NOT NULL
                   THEN cantidad - TRY_CAST(@inst AS DECIMAL(18,2))
                   ELSE balance END
WHERE id = @id;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@desc",   SqlDbType.NVarChar, 500).Value = (object?)NullIfEmpty(editDescripcion) ?? DBNull.Value;
        cmd.Parameters.Add("@fecha",  SqlDbType.Date).Value          = (object?)(editFecha?.Date) ?? DBNull.Value;
        cmd.Parameters.Add("@nosal",  SqlDbType.NVarChar, 100).Value = (object?)NullIfEmpty(editNoSalida) ?? DBNull.Value;
        cmd.Parameters.Add("@recibe", SqlDbType.NVarChar, 200).Value = (object?)NullIfEmpty(editRecibe) ?? DBNull.Value;
        cmd.Parameters.Add("@inst",   SqlDbType.NVarChar, 200).Value = (object?)NullIfEmpty(editInstalado) ?? DBNull.Value;
        cmd.Parameters.Add("@proy",   SqlDbType.NVarChar, 200).Value = (object?)NullIfEmpty(editProyecto) ?? DBNull.Value;
        cmd.Parameters.Add("@obs",    SqlDbType.NVarChar, 500).Value = (object?)NullIfEmpty(editObs) ?? DBNull.Value;
        cmd.Parameters.Add("@id",     SqlDbType.Int).Value           = editId;

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "No se pudo editar la salida {Id}", editId);
        }

        return RedirectToPage("/SalidaMaterial", new { edited = 1 });
    }

    private async Task CargarUltimasAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Obtener todos los registros con filtros (sin paginación)
        var sql = @"
SELECT
    id,
    cod_fab,
    descripcion,
    cantidad,
    um,
    fecha_salida,
    no_salida,
    recibe,
    instalado,
    balance,
    obs,
    proyecto_asignado
FROM inventario.salidas_inventario
WHERE 1=1
  AND (@filtCodigo   IS NULL OR cod_fab            LIKE '%'+@filtCodigo+'%')
  AND (@filtDesc     IS NULL OR descripcion         LIKE '%'+@filtDesc+'%')
  AND (@filtCantidad IS NULL OR cantidad            = @filtCantidad)
  AND (@filtFecha    IS NULL OR CAST(fecha_salida AS DATE) = @filtFecha)
  AND (@filtProyecto IS NULL OR proyecto_asignado   LIKE '%'+@filtProyecto+'%')
ORDER BY fecha_salida DESC, id DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@filtCodigo",   SqlDbType.NVarChar, 200).Value = (object?)FiltCodigo   ?? DBNull.Value;
        cmd.Parameters.Add("@filtDesc",      SqlDbType.NVarChar, 500).Value = (object?)FiltDesc     ?? DBNull.Value;
        cmd.Parameters.Add("@filtCantidad",  SqlDbType.Decimal).Value       = (object?)FiltCantidad ?? DBNull.Value;
        cmd.Parameters.Add("@filtFecha",     SqlDbType.Date).Value          = (object?)FiltFecha    ?? DBNull.Value;
        cmd.Parameters.Add("@filtProyecto",  SqlDbType.NVarChar, 200).Value = (object?)FiltProyecto ?? DBNull.Value;

        await using var rdr = await cmd.ExecuteReaderAsync();

        var ordId         = rdr.GetOrdinal("id");
        var ordCodFab     = rdr.GetOrdinal("cod_fab");
        var ordDesc       = rdr.GetOrdinal("descripcion");
        var ordCant       = rdr.GetOrdinal("cantidad");
        var ordUm         = rdr.GetOrdinal("um");
        var ordFecha      = rdr.GetOrdinal("fecha_salida");
        var ordNoSalida   = rdr.GetOrdinal("no_salida");
        var ordRecibe     = rdr.GetOrdinal("recibe");
        var ordInst       = rdr.GetOrdinal("instalado");
        var ordBalance    = rdr.GetOrdinal("balance");
        var ordObs        = rdr.GetOrdinal("obs");
        var ordProyecto   = rdr.GetOrdinal("proyecto_asignado");

        Ultimas.Clear();

        while (await rdr.ReadAsync())
        {
            Ultimas.Add(new SalidaRow
            {
                Id               = rdr.GetInt32(ordId),
                CodFab           = rdr.IsDBNull(ordCodFab)   ? "" : rdr.GetString(ordCodFab),
                Descripcion      = rdr.IsDBNull(ordDesc)     ? "" : rdr.GetString(ordDesc),
                Cantidad         = rdr.GetDecimal(ordCant),
                Um               = rdr.IsDBNull(ordUm)       ? "" : rdr.GetString(ordUm),
                FechaSalida      = rdr.GetDateTime(ordFecha),
                NoSalida         = rdr.IsDBNull(ordNoSalida) ? null : rdr.GetString(ordNoSalida),
                Recibe           = rdr.IsDBNull(ordRecibe)   ? "" : rdr.GetString(ordRecibe),
                Instalado        = rdr.IsDBNull(ordInst)     ? "" : rdr.GetString(ordInst),
                Balance          = rdr.GetDecimal(ordBalance),
                Obs              = rdr.IsDBNull(ordObs)      ? null : rdr.GetString(ordObs),
                ProyectoAsignado = rdr.IsDBNull(ordProyecto) ? null : rdr.GetString(ordProyecto),
            });
        }
        TotalRecords = Ultimas.Count;
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class SalidaForm
    {
        public string CodFab      { get; set; } = "";
        public string CodInt      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal? Cantidad  { get; set; }
        public string Um          { get; set; } = "";
        public DateTime? FechaSalida { get; set; } = DateTime.Today;
        public string? NoSalida   { get; set; }
        public string Recibe      { get; set; } = "";
        public string Instalado   { get; set; } = "";
        public string? Obs        { get; set; }
        public string? ProyectoAsignado { get; set; }
        public string? Responsable      { get; set; }
    }

    public class SalidaRow
    {
        public int Id           { get; set; }
        public string CodFab    { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal Cantidad { get; set; }
        public string Um        { get; set; } = "";
        public DateTime FechaSalida { get; set; }
        public string? NoSalida { get; set; }
        public string Recibe    { get; set; } = "";
        public string Instalado { get; set; } = "";
        public decimal Balance  { get; set; }
        public string? Obs      { get; set; }
        public string? ProyectoAsignado { get; set; }
        public string? Responsable      { get; set; }
    }
}
