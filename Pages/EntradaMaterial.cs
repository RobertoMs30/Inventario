using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class EntradaMaterialModel : PageBase
{
    private readonly IConfiguration _config;

    private readonly ILogger<EntradaMaterialModel> _logger;

    public EntradaMaterialModel(IConfiguration config, ILogger<EntradaMaterialModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Formulario
    [BindProperty]
    public EntradaForm Form { get; set; } = new();

    // Mini catálogo (panel derecho)
    public List<MiniCatRow> CatalogoMini { get; private set; } = new();

    // Últimas entradas
    public List<EntradaRow> Ultimas { get; private set; } = new();

    // Paginación
    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 20;
    public int TotalRecords { get; private set; }
    public int TotalPages => TotalRecords > 0 ? (TotalRecords + PageSize - 1) / PageSize : 1;

    public bool ShowDevolucion { get; private set; }

    [BindProperty]
    public DevolucionForm DevForm { get; set; } = new();

    // Historial devoluciones
    public List<DevolucionRow> UltimasDev { get; private set; } = new();
    public int TotalRecordsDev { get; private set; }

    [BindProperty(SupportsGet = true)] public string? HistMode       { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevCodigo  { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevDesc    { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevFecha   { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevProy    { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDevDevuelve{ get; set; }

    // Filtros historial
    // Listas para selects del formulario
    public List<string> Proveedores   { get; private set; } = new();
    public List<string> OrdenesCompra { get; private set; } = new();
    public List<string> Proyectos     { get; private set; } = new();

    // Filtros historial
    [BindProperty(SupportsGet = true)] public string? FiltCodigo    { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltDesc      { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltCantidad  { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltFecha     { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltProveedor { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltNoPo      { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltFac       { get; set; }

    // ── Autocomplete: sugerencias para filtros del historial ──
    private async Task<List<string>> SugDistinctAsync(string col, string table, string? q, string? notNullCol = null)
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        string nullFilter = notNullCol != null ? $"{notNullCol} IS NOT NULL AND " : "";
        string whereClause = string.IsNullOrWhiteSpace(q)
            ? $"WHERE {nullFilter}{col} IS NOT NULL"
            : $"WHERE {nullFilter}{col} LIKE '%'+@q+'%'";
        var sql = $"SELECT DISTINCT TOP 10 {col} FROM {table} {whereClause} ORDER BY {col}";
        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(q))
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            if (!rdr.IsDBNull(0)) list.Add(rdr.GetString(0));
        return list;
    }

    public async Task<IActionResult> OnGetSugCodigoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("cod_fab",     "inventario.entradas_inventario", q));
    }

    public async Task<IActionResult> OnGetSugDescAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("descripcion", "inventario.entradas_inventario", q));
    }

    public async Task<IActionResult> OnGetSugProvAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("proveedor",   "inventario.entradas_inventario", q));
    }

    public async Task<IActionResult> OnGetSugNoPoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("no_po",       "inventario.entradas_inventario", q, "no_po"));
    }

    public async Task<IActionResult> OnGetSugFacAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("fac",         "inventario.entradas_inventario", q, "fac"));
    }

    public async Task<IActionResult> OnGetSugProyectoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return new JsonResult(new List<object>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT TOP 10 CAST(cotizacion AS NVARCHAR(50)), ISNULL(proyecto,'') FROM administracion_proyectos.proyectos WHERE CAST(cotizacion AS NVARCHAR(50)) LIKE @q ORDER BY cotizacion DESC;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = "%" + q.Trim() + "%";
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            if (!rdr.IsDBNull(0)) list.Add(new { num = rdr.GetString(0), nombre = rdr.GetString(1) });
        return new JsonResult(list);
    }

    public async Task<IActionResult> OnGetSugDevuelveAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("devuelve", "inventario.devoluciones_inventario", q, "devuelve"));
    }

    public async Task<IActionResult> OnGetSugDevCodigoAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("cod_fab",     "inventario.devoluciones_inventario", q));
    }
    public async Task<IActionResult> OnGetSugDevDescAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("descripcion", "inventario.devoluciones_inventario", q));
    }
    public async Task<IActionResult> OnGetSugDevProyAsync(string? q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        return new JsonResult(await SugDistinctAsync("proyecto",    "inventario.devoluciones_inventario", q, "proyecto"));
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
                ISNULL(c.cant,      0)             AS existencia,
                ISNULL(c.marca,     '')            AS marca
            FROM inventario.catalogo_materiales c
            LEFT JOIN inventario.entradas_inventario ei
                   ON LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(ei.cod_fab))
            WHERE c.cod_fab     LIKE '%' + @q + '%'
               OR ei.cod_int    LIKE '%' + @q + '%'
               OR c.descripcion LIKE '%' + @q + '%'
               OR c.no_part     LIKE '%' + @q + '%'
            GROUP BY c.cod_fab, c.no_part, c.descripcion,
                     c.um, c.partida, c.proveedor, c.pu, c.moneda, c.cant, c.marca
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
                marca       = rdr.IsDBNull(10) ? "" : rdr.GetString(10),
            });
        }

        return new JsonResult(results);
    }

    public async Task<IActionResult> OnGetAsync(int page = 1)
    {
        var deny = VerificarAcceso("inventario");
        if (deny !=null) return deny;
        ModelState.Clear();

        if (Form.FechaCompra is null)
            Form.FechaCompra = DateTime.Today;
        if (DevForm.FechaDevolucion is null)
            DevForm.FechaDevolucion = DateTime.Today;
        if (Request.Query["mode"] == "devolucion")
            ShowDevolucion = true;

        if (page < 1) page = 1;
        CurrentPage = page;

        Form.CodInt = await GenerarSiguienteCodIntAsync();

        await Task.WhenAll(CargarUltimasAsync(), CargarUltimasDevAsync(),
                           CargarProveedoresAsync(), CargarOrdenesCompraAsync(), CargarProyectosAsync());
        return Page();
    }

    private async Task CargarProveedoresAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Detectar nombre de columna igual que OrdenesCompra
            var sqlCols = @"SELECT name FROM sys.columns
                            WHERE object_id = OBJECT_ID('inventario.cat_proveedores')
                              AND name IN ('nombre_comercial','razon_social','nombre','proveedor','nombre_proveedor')";
            await using var cmdCols = new SqlCommand(sqlCols, conn);
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var rdrCols = await cmdCols.ExecuteReaderAsync();
            while (await rdrCols.ReadAsync()) cols.Add(rdrCols.GetString(0));
            await rdrCols.CloseAsync();

            if (cols.Count == 0) return;

            string nombreCol = cols.Contains("nombre_comercial") ? "nombre_comercial" :
                               cols.Contains("razon_social")     ? "razon_social"     :
                               cols.Contains("nombre")           ? "nombre"           :
                               cols.Contains("proveedor")        ? "proveedor"        : "nombre";

            var sql = $@"SELECT DISTINCT ISNULL({nombreCol}, '') AS nombre
                         FROM inventario.cat_proveedores
                         WHERE {nombreCol} IS NOT NULL AND LTRIM(RTRIM({nombreCol})) <> ''
                         ORDER BY nombre";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) Proveedores.Add(rdr.GetString(0).Trim());
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar la lista de proveedores"); }
    }

    private async Task CargarOrdenesCompraAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var sql = @"SELECT DISTINCT ISNULL(oc, '') AS oc
                        FROM administracion_proyectos.ordenes_compra
                        WHERE oc IS NOT NULL AND LTRIM(RTRIM(oc)) <> ''
                        ORDER BY oc DESC";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) OrdenesCompra.Add(rdr.GetString(0).Trim());
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar la lista de órdenes de compra"); }
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
        var sql = @"SELECT MAX(v) + 1 FROM (
                        SELECT ISNULL(MAX(TRY_CAST(cod_int AS BIGINT)), 0) AS v
                        FROM inventario.entradas_inventario
                        UNION ALL
                        SELECT 3005204
                    ) AS t";
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "3000001";
    }

    private async Task CargarCatalogoMiniAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
SELECT cod_fab, ISNULL(descripcion,'') AS descripcion,
       ISNULL(um,'') AS um, ISNULL(cant,0) AS cant
FROM inventario.catalogo_materiales
ORDER BY cod_fab;";

        await using var cmd = new SqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        CatalogoMini.Clear();
        while (await rdr.ReadAsync())
        {
            CatalogoMini.Add(new MiniCatRow
            {
                CodFab      = rdr.GetString(0),
                Descripcion = rdr.GetString(1),
                Um          = rdr.GetString(2),
                Cant        = rdr.GetDecimal(3),
            });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        // Limpiar errores automáticos del framework para campos numéricos/fecha
        ModelState.Remove("Form.Cantidad");
        ModelState.Remove("Form.FechaCompra");
        ModelState.Remove("Form.Um");
        ModelState.Remove("Form.Proveedor");
        ModelState.Remove("Form.Descripcion");
        ModelState.Remove("Form.CodFab");
        ModelState.Remove("Form.CodInt");
        ModelState.Remove("Form.NoPart");
        ModelState.Remove("Form.Partida");
        ModelState.Remove("Form.Moneda");
        ModelState.Remove("Form.Marca");

        // 1) Validaciones base (siempre)
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

        if (Form.FechaCompra is null)
            ModelState.AddModelError("", "Fecha compra es obligatoria.");

        if (string.IsNullOrWhiteSpace(Form.Proveedor))
            ModelState.AddModelError("", "Proveedor es obligatorio.");

        // Si ya hay errores básicos, no seguimos
        if (!ModelState.IsValid)
        {
            await Task.WhenAll(CargarUltimasAsync(), CargarUltimasDevAsync());
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");

        // 2) Verificar en BD si existe
        bool existeEnCatalogo;
        await using (var connCheck = new SqlConnection(connStr))
        {
            await connCheck.OpenAsync();
            var sqlExiste = @"SELECT CASE WHEN EXISTS (
                SELECT 1 FROM inventario.catalogo_materiales
                WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab))
            ) THEN 1 ELSE 0 END;";
            await using var cmdExiste = new SqlCommand(sqlExiste, connCheck);
            cmdExiste.Parameters.AddWithValue("@cod_fab", (Form.CodFab ?? "").Trim());
            var r = await cmdExiste.ExecuteScalarAsync();
            existeEnCatalogo = Convert.ToInt32(r) == 1;
        }

        // 3) Si NO existe, exigir campos del catálogo
        if (!existeEnCatalogo)
        {
            if (string.IsNullOrWhiteSpace(Form.Partida))
                ModelState.AddModelError("", "Partida es obligatoria para material nuevo.");

            if (Form.Pu is null || Form.Pu <= 0)
                ModelState.AddModelError("", "PU es obligatorio (y debe ser > 0) para material nuevo.");

            if (string.IsNullOrWhiteSpace(Form.Moneda))
                ModelState.AddModelError("", "Moneda es obligatoria para material nuevo.");
        }

        if (!ModelState.IsValid)
        {
            await CargarUltimasAsync();
            return Page();
        }

        // 4) Ejecutar SP: inserta bitácora y actualiza/crea catálogo
        await using var connSp = new SqlConnection(connStr);
        await connSp.OpenAsync();
        await using var cmd = new SqlCommand("dbo.sp_RegistrarEntradaMaterial", connSp);
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.AddWithValue("@cod_fab", Form.CodFab.Trim());
        cmd.Parameters.AddWithValue("@cod_int", Form.CodInt.Trim());
        cmd.Parameters.AddWithValue("@descripcion", Form.Descripcion.Trim());
        cmd.Parameters.AddWithValue("@cantidad", Form.Cantidad!.Value);
        cmd.Parameters.AddWithValue("@um", Form.Um.Trim());
        cmd.Parameters.AddWithValue("@fecha_compra", Form.FechaCompra!.Value.Date);
        cmd.Parameters.AddWithValue("@proveedor", Form.Proveedor.Trim());

        // Opcionales (entradas_inventario)
        cmd.Parameters.AddWithValue("@no_po",     (object?)NullIfEmpty(Form.NoPo)      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fac",       (object?)NullIfEmpty(Form.Fac)       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@diferencia",(object?)NullIfEmpty(Form.Diferencia)?? DBNull.Value);
        cmd.Parameters.AddWithValue("@proyecto",  (object?)NullIfEmpty(Form.Proyecto)  ?? DBNull.Value);

        // Solo aplican para material NUEVO (si existe, pueden ir NULL)
        cmd.Parameters.AddWithValue("@no_part", (object?)NullIfEmpty(Form.NoPart) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@partida", (object?)NullIfEmpty(Form.Partida) ?? DBNull.Value);

        // PU / Moneda (obligatorios SOLO si es material nuevo)
        var pPu = cmd.Parameters.Add("@pu", SqlDbType.Decimal);
        pPu.Precision = 18;
        pPu.Scale = 2;
        pPu.Value = !existeEnCatalogo ? (object)Form.Pu!.Value : DBNull.Value;

        cmd.Parameters.Add("@moneda", SqlDbType.NVarChar, 50).Value =
            !existeEnCatalogo ? (object?)NullIfEmpty(Form.Moneda) ?? DBNull.Value : DBNull.Value;

        try
        {
            await cmd.ExecuteNonQueryAsync();

            // Actualizar marca si se proporcionó
            if (!string.IsNullOrWhiteSpace(Form.Marca))
            {
                var sqlMarca = "UPDATE inventario.catalogo_materiales SET marca = @marca WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));";
                await using var cmdMarca = new SqlCommand(sqlMarca, connSp);
                cmdMarca.Parameters.Add("@marca",   SqlDbType.NVarChar, 100).Value = Form.Marca.Trim();
                cmdMarca.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = Form.CodFab.Trim();
                await cmdMarca.ExecuteNonQueryAsync();
            }

            // Si material existente y el usuario quiere actualizar el precio
            if (existeEnCatalogo && Form.NuevoPu.HasValue && Form.NuevoPu > 0)
            {
                var sqlUpdatePu = @"
UPDATE inventario.catalogo_materiales
SET pu     = @pu,
    moneda = CASE WHEN @moneda IS NOT NULL AND @moneda <> '' THEN @moneda ELSE moneda END
WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));";

                await using var cmdPu = new SqlCommand(sqlUpdatePu, connSp);
                var pNuevoPu = cmdPu.Parameters.Add("@pu", SqlDbType.Decimal);
                pNuevoPu.Precision = 18;
                pNuevoPu.Scale = 2;
                pNuevoPu.Value = Form.NuevoPu.Value;
                cmdPu.Parameters.Add("@moneda", SqlDbType.NVarChar, 10).Value =
                    (object?)NullIfEmpty(Form.NuevaMoneda) ?? DBNull.Value;
                cmdPu.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = Form.CodFab.Trim();
                await cmdPu.ExecuteNonQueryAsync();
            }
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", $"Error al guardar en base de datos: {ex.Message}");
            await Task.WhenAll(CargarUltimasAsync(), CargarUltimasDevAsync());
            return Page();
        }

        return RedirectToPage("/EntradaMaterial", new { saved = 1 });
    }

    public async Task<IActionResult> OnPostDevolucionAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        ModelState.Remove("DevForm.FechaDevolucion");
        ModelState.Remove("DevForm.CodFab");
        ModelState.Remove("DevForm.CodInt");
        ModelState.Remove("DevForm.Descripcion");
        ModelState.Remove("DevForm.Um");
        ModelState.Remove("DevForm.Cantidad");

        if (string.IsNullOrWhiteSpace(DevForm.CodFab))
            ModelState.AddModelError("", "Código es obligatorio.");
        if (string.IsNullOrWhiteSpace(DevForm.Descripcion))
            ModelState.AddModelError("", "Descripción es obligatoria.");
        if (DevForm.Cantidad is null || DevForm.Cantidad <= 0)
            ModelState.AddModelError("", "Cantidad debe ser mayor a 0.");
        if (DevForm.FechaDevolucion is null)
            ModelState.AddModelError("", "Fecha es obligatoria.");

        if (!ModelState.IsValid)
        {
            ShowDevolucion = true;
            await Task.WhenAll(CargarUltimasAsync(), CargarUltimasDevAsync());
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_RegistrarDevolucion", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@cod_fab",          DevForm.CodFab!.Trim());
        cmd.Parameters.AddWithValue("@cod_int",          (object?)NullIfEmpty(DevForm.CodInt) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@descripcion",      DevForm.Descripcion!.Trim());
        cmd.Parameters.AddWithValue("@cantidad",         DevForm.Cantidad!.Value);
        cmd.Parameters.AddWithValue("@um",               (object?)NullIfEmpty(DevForm.Um) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fecha_devolucion", DevForm.FechaDevolucion!.Value.Date);
        cmd.Parameters.AddWithValue("@motivo",           (object?)NullIfEmpty(DevForm.Motivo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@devuelve",         (object?)NullIfEmpty(DevForm.Devuelve) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@proyecto",         (object?)NullIfEmpty(DevForm.Proyecto) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obs",              (object?)NullIfEmpty(DevForm.Obs) ?? DBNull.Value);

        try { await cmd.ExecuteNonQueryAsync(); }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", $"Error al guardar: {ex.Message}");
            ShowDevolucion = true;
            await Task.WhenAll(CargarUltimasAsync(), CargarUltimasDevAsync());
            return Page();
        }

        return RedirectToPage("/EntradaMaterial", new { saved = 2 });
    }

    private async Task CargarUltimasAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Construir WHERE dinámico
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(FiltCodigo))    clauses.Add("e.cod_fab     LIKE '%'+@fCod+'%'");
        if (!string.IsNullOrEmpty(FiltDesc))      clauses.Add("e.descripcion LIKE '%'+@fDesc+'%'");
        if (!string.IsNullOrEmpty(FiltCantidad))  clauses.Add("CAST(e.cantidad AS NVARCHAR) = @fCant");
        if (!string.IsNullOrEmpty(FiltFecha))     clauses.Add("CONVERT(date, e.fecha_compra) = CAST(@fFecha AS date)");
        if (!string.IsNullOrEmpty(FiltProveedor)) clauses.Add("e.proveedor   LIKE '%'+@fProv+'%'");
        if (!string.IsNullOrEmpty(FiltNoPo))      clauses.Add("e.no_po       LIKE '%'+@fNoPo+'%'");
        if (!string.IsNullOrEmpty(FiltFac))       clauses.Add("e.fac         LIKE '%'+@fFac+'%'");
        string wc = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        var sql = $@"
SELECT e.id, e.cod_fab, e.descripcion, e.cantidad, e.um, e.proveedor, e.fecha_compra, e.no_po,
       ISNULL(c.marca, '') AS marca
FROM inventario.entradas_inventario e
LEFT JOIN inventario.catalogo_materiales c ON LTRIM(RTRIM(e.cod_fab)) = LTRIM(RTRIM(c.cod_fab))
{wc}
ORDER BY e.id DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        AddFilterParams(cmd);
        TotalRecords = 0;

        await using var rdr = await cmd.ExecuteReaderAsync();
        var ordId    = rdr.GetOrdinal("id");
        var ordCod   = rdr.GetOrdinal("cod_fab");
        var ordDesc  = rdr.GetOrdinal("descripcion");
        var ordCant  = rdr.GetOrdinal("cantidad");
        var ordUm    = rdr.GetOrdinal("um");
        var ordProv  = rdr.GetOrdinal("proveedor");
        var ordFecha = rdr.GetOrdinal("fecha_compra");
        var ordNoPo  = rdr.GetOrdinal("no_po");
        var ordMarca = rdr.GetOrdinal("marca");

        Ultimas.Clear();
        while (await rdr.ReadAsync())
        {
            Ultimas.Add(new EntradaRow
            {
                Id          = rdr.GetInt32(ordId),
                CodFab      = rdr.IsDBNull(ordCod)   ? "" : rdr.GetString(ordCod),
                Descripcion = rdr.IsDBNull(ordDesc)  ? "" : rdr.GetString(ordDesc),
                Cantidad    = rdr.GetDecimal(ordCant),
                Um          = rdr.IsDBNull(ordUm)    ? "" : rdr.GetString(ordUm),
                Proveedor   = rdr.IsDBNull(ordProv)  ? "" : rdr.GetString(ordProv),
                FechaCompra = rdr.GetDateTime(ordFecha),
                NoPo        = rdr.IsDBNull(ordNoPo)  ? null : rdr.GetString(ordNoPo),
                Marca       = rdr.IsDBNull(ordMarca) ? null : rdr.GetString(ordMarca),
            });
        }
        TotalRecords = Ultimas.Count;
    }

    private async Task CargarUltimasDevAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(FiltDevCodigo))   clauses.Add("cod_fab     LIKE '%'+@fCod+'%'");
        if (!string.IsNullOrEmpty(FiltDevDesc))     clauses.Add("descripcion LIKE '%'+@fDesc+'%'");
        if (!string.IsNullOrEmpty(FiltDevFecha))    clauses.Add("CONVERT(date, fecha_devolucion) = CAST(@fFecha AS date)");
        if (!string.IsNullOrEmpty(FiltDevProy))     clauses.Add("proyecto    LIKE '%'+@fProy+'%'");
        if (!string.IsNullOrEmpty(FiltDevDevuelve)) clauses.Add("devuelve    LIKE '%'+@fDev+'%'");
        string wc = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        var sql = $@"
SELECT id, cod_fab, descripcion, cantidad, um, fecha_devolucion, motivo, devuelve, proyecto
FROM inventario.devoluciones_inventario
{wc}
ORDER BY id DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(FiltDevCodigo))   cmd.Parameters.Add("@fCod",   SqlDbType.NVarChar, 300).Value = FiltDevCodigo.Trim();
        if (!string.IsNullOrEmpty(FiltDevDesc))     cmd.Parameters.Add("@fDesc",  SqlDbType.NVarChar, 300).Value = FiltDevDesc.Trim();
        if (!string.IsNullOrEmpty(FiltDevFecha))    cmd.Parameters.Add("@fFecha", SqlDbType.NVarChar, 20).Value  = FiltDevFecha.Trim();
        if (!string.IsNullOrEmpty(FiltDevProy))     cmd.Parameters.Add("@fProy",  SqlDbType.NVarChar, 300).Value = FiltDevProy.Trim();
        if (!string.IsNullOrEmpty(FiltDevDevuelve)) cmd.Parameters.Add("@fDev",   SqlDbType.NVarChar, 300).Value = FiltDevDevuelve.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        UltimasDev.Clear();
        while (await rdr.ReadAsync())
        {
            UltimasDev.Add(new DevolucionRow
            {
                Id              = rdr.GetInt32(0),
                CodFab          = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                Descripcion     = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                Cantidad        = rdr.GetDecimal(3),
                Um              = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                FechaDevolucion = rdr.GetDateTime(5),
                Motivo          = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                Devuelve        = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                Proyecto        = rdr.IsDBNull(8) ? null : rdr.GetString(8),
            });
        }
        TotalRecordsDev = UltimasDev.Count;
    }

    private void AddFilterParams(SqlCommand cmd)
    {
        if (!string.IsNullOrEmpty(FiltCodigo))    cmd.Parameters.Add("@fCod",   SqlDbType.NVarChar, 300).Value = FiltCodigo.Trim();
        if (!string.IsNullOrEmpty(FiltDesc))      cmd.Parameters.Add("@fDesc",  SqlDbType.NVarChar, 300).Value = FiltDesc.Trim();
        if (!string.IsNullOrEmpty(FiltCantidad))  cmd.Parameters.Add("@fCant",  SqlDbType.NVarChar, 50).Value  = FiltCantidad.Trim();
        if (!string.IsNullOrEmpty(FiltFecha))     cmd.Parameters.Add("@fFecha", SqlDbType.NVarChar, 20).Value  = FiltFecha.Trim();
        if (!string.IsNullOrEmpty(FiltProveedor)) cmd.Parameters.Add("@fProv",  SqlDbType.NVarChar, 300).Value = FiltProveedor.Trim();
        if (!string.IsNullOrEmpty(FiltNoPo))      cmd.Parameters.Add("@fNoPo",  SqlDbType.NVarChar, 300).Value = FiltNoPo.Trim();
        if (!string.IsNullOrEmpty(FiltFac))       cmd.Parameters.Add("@fFac",   SqlDbType.NVarChar, 300).Value = FiltFac.Trim();
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class EntradaForm
    {
        public string CodFab { get; set; } = "";
        public string CodInt { get; set; } = "";
        public bool IsExisting { get; set; } = false;

        // Solo obligatorios si el material es nuevo en catálogo
        public string NoPart { get; set; } = "";
        public string Partida { get; set; } = "";
        public string? Marca { get; set; }

        public string Descripcion { get; set; } = "";
        public decimal? Cantidad { get; set; }
        public string Um { get; set; } = "";
        public DateTime? FechaCompra { get; set; } = DateTime.Today;
        public string Proveedor { get; set; } = "";

        public string? NoPo { get; set; }
        public string? Fac { get; set; }
        public string? Diferencia { get; set; }
        public string? Proyecto { get; set; }

        public decimal? Pu { get; set; }
        public string? Moneda { get; set; }

        // Actualización de precio para materiales existentes (opcional)
        public decimal? NuevoPu { get; set; }
        public string? NuevaMoneda { get; set; }
    }

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

    public class MiniCatRow
    {
        public string CodFab      { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Um          { get; set; } = "";
        public decimal Cant       { get; set; }
    }

    public class EntradaRow
    {
        public int Id { get; set; }
        public string CodFab { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal Cantidad { get; set; }
        public string Um { get; set; } = "";
        public string Proveedor { get; set; } = "";
        public DateTime FechaCompra { get; set; }
        public string? NoPo { get; set; }
        public string? Marca { get; set; }
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
    }
}
