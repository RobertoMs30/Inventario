using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace InventarioWeb.Pages;

public class OrdenesCompraModel : PageBase
{
    private readonly IConfiguration _config;

    private readonly ILogger<OrdenesCompraModel> _logger;

    public OrdenesCompraModel(IConfiguration config, ILogger<OrdenesCompraModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    [BindProperty]
    public OrdenCompraForm Form { get; set; } = new();

    public List<OrdenCompraRow> Ultimas    { get; private set; } = new();
    public List<string>         Proveedores { get; private set; } = new();
    public List<string>         Empleados   { get; private set; } = new();
    public List<string>         Proyectos   { get; private set; } = new();
    public int TotalRecords { get; private set; }

    // Filtros
    [BindProperty(SupportsGet = true)] public string? FiltOc         { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltProveedor  { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltElaboro    { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltFecha      { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltProyecto   { get; set; }
    [BindProperty(SupportsGet = true)] public string? FiltCotizacion { get; set; }

    // ── Autocomplete filtros historial ──
    public async Task<IActionResult> OnGetSugOcAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("oc", q));
    }
    public async Task<IActionResult> OnGetSugProveedorAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("proveedor", q));
    }
    public async Task<IActionResult> OnGetSugElaboroAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("elaboro", q));
    }
    public async Task<IActionResult> OnGetSugProyectoAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("proyecto", q));
    }
    public async Task<IActionResult> OnGetSugCotizacionAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        return new JsonResult(await SugAsync("cotizacion", q));
    }

    private async Task<List<string>> SugAsync(string col, string? q)
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        string where = string.IsNullOrWhiteSpace(q)
            ? $"WHERE {col} IS NOT NULL"
            : $"WHERE {col} LIKE '%'+@q+'%'";
        var sql = $"SELECT DISTINCT TOP 10 {col} FROM administracion_proyectos.ordenes_compra {where} ORDER BY {col}";
        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(q))
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            if (!rdr.IsDBNull(0)) list.Add(rdr.GetString(0));
        return list;
    }

    // ── Búsqueda catálogo para líneas de detalle ──
    public async Task<IActionResult> OnGetBuscarCatalogoAsync(string? q)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q))
            return new JsonResult(new List<object>());
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"
            SELECT TOP 12 cod_fab, ISNULL(descripcion,'') AS descripcion,
                          ISNULL(um,'') AS um, ISNULL(pu, 0) AS pu,
                          ISNULL(cant, 0) AS cant
            FROM inventario.catalogo_materiales
            WHERE cod_fab     LIKE '%' + @q + '%'
               OR descripcion LIKE '%' + @q + '%'
               OR no_part     LIKE '%' + @q + '%'
            ORDER BY cod_fab";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new {
                codFab      = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                descripcion = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                um          = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                pu          = rdr.IsDBNull(3) ? 0d : (double)rdr.GetDecimal(3),
                cant        = rdr.IsDBNull(4) ? 0d : (double)rdr.GetDecimal(4),
            });
        return new JsonResult(list);
    }

    // ── Cargar orden para editar (modal) ──
    public async Task<IActionResult> OnGetCargarOrdenAsync(int id)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        bool tieneReviso = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "reviso");
        bool tieneAprobo = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "aprobo");
        string revisoColE = tieneReviso ? ", ISNULL(reviso,'')" : ", ''";
        string aproboColE = tieneAprobo ? ", ISNULL(aprobo,'')" : ", ''";

        var sql = $@"SELECT id, oc, proveedor, elaboro, fecha, proyecto, cotizacion,
                            ISNULL(num_requisicion,''){revisoColE}{aproboColE}
                     FROM administracion_proyectos.ordenes_compra WHERE id = @id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return new JsonResult(null);

        var header = new
        {
            id             = rdr.GetInt32(0),
            oc             = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
            proveedor      = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
            elaboro        = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
            fecha          = rdr.IsDBNull(4) ? "" : rdr.GetDateTime(4).ToString("yyyy-MM-dd"),
            proyecto       = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
            cotizacion     = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
            numRequisicion = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
            reviso         = rdr.IsDBNull(8) ? "" : rdr.GetString(8),
            aprobo         = rdr.IsDBNull(9) ? "" : rdr.GetString(9),
        };
        await rdr.CloseAsync();

        // Líneas de detalle
        var sqlDet = @"SELECT no_articulo, ISNULL(cod_material,''), ISNULL(descripcion,''),
                              ISNULL(cantidad,0), ISNULL(tipo_unidad,''),
                              ISNULL(precio_unitario,0), ISNULL(importe_total,0)
                       FROM administracion_proyectos.ordenes_compra_detalle
                       WHERE id_oc = @id ORDER BY no_articulo";
        await using var cmdDet = new SqlCommand(sqlDet, conn);
        cmdDet.Parameters.AddWithValue("@id", id);
        var items = new List<object>();
        await using var rdrDet = await cmdDet.ExecuteReaderAsync();
        while (await rdrDet.ReadAsync())
            items.Add(new {
                noArticulo     = rdrDet.GetInt32(0),
                codMaterial    = rdrDet.GetString(1),
                descripcion    = rdrDet.GetString(2),
                cantidad       = (double)rdrDet.GetDecimal(3),
                tipoUnidad     = rdrDet.GetString(4),
                precioUnitario = (double)rdrDet.GetDecimal(5),
                importeTotal   = (double)rdrDet.GetDecimal(6),
            });

        return new JsonResult(new { header, items });
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("compras");
        if (deny != null) return deny;

        ModelState.Clear();
        if (Form.Fecha is null) Form.Fecha = DateTime.Today;
        await Task.WhenAll(CargarUltimasAsync(), CargarProveedoresAsync(), CargarEmpleadosAsync(), CargarProyectosAsync());
        if (string.IsNullOrWhiteSpace(Form.Oc))
            Form.Oc = await SiguienteOcAsync();

        return Page();
    }

    private async Task<string> SiguienteOcAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            // Toma el OC del último registro (por id) que sea un número entero válido
            var sql = @"SELECT TOP 1 LTRIM(RTRIM(oc))
                        FROM administracion_proyectos.ordenes_compra
                        WHERE TRY_CAST(LTRIM(RTRIM(oc)) AS INT) IS NOT NULL
                        ORDER BY id DESC";
            await using var cmd = new SqlCommand(sql, conn);
            var val = await cmd.ExecuteScalarAsync();
            if (val != null && int.TryParse(val.ToString()?.Trim(), out int ultimo))
                return (ultimo + 1).ToString();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo calcular el siguiente número de OC"); }
        return "";
    }

    // ── Registrar nueva OC ──
    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("compras");
        if (deny != null) return deny;

        if (string.IsNullOrWhiteSpace(Form.Oc))
            ModelState.AddModelError("", "Número de OC es obligatorio.");
        if (Form.Fecha is null)
            ModelState.AddModelError("", "Fecha es obligatoria.");

        if (!ModelState.IsValid)
        {
            await Task.WhenAll(CargarUltimasAsync(), CargarProveedoresAsync(), CargarEmpleadosAsync(), CargarProyectosAsync());
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"INSERT INTO administracion_proyectos.ordenes_compra
                        (oc, proveedor, elaboro, fecha, proyecto, cotizacion, num_requisicion, reviso, aprobo, estado)
                    OUTPUT INSERTED.id
                    VALUES (@oc, @proveedor, @elaboro, @fecha, @proyecto, @cotizacion, @numReq, @reviso, @aprobo, 'falta_pago')";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@oc",         SqlDbType.NVarChar, 100).Value  = (object?)NullIfEmpty(Form.Oc)             ?? DBNull.Value;
        cmd.Parameters.Add("@proveedor",  SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Form.Proveedor)      ?? DBNull.Value;
        cmd.Parameters.Add("@elaboro",    SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Form.Elaboro)        ?? DBNull.Value;
        cmd.Parameters.Add("@fecha",      SqlDbType.Date).Value           = (object?)Form.Fecha?.Date                 ?? DBNull.Value;
        cmd.Parameters.Add("@proyecto",   SqlDbType.NVarChar, -1).Value   = (object?)NullIfEmpty(Form.Proyecto)       ?? DBNull.Value;
        cmd.Parameters.Add("@cotizacion", SqlDbType.NVarChar, -1).Value   = (object?)NullIfEmpty(Form.Cotizacion)     ?? DBNull.Value;
        cmd.Parameters.Add("@numReq",     SqlDbType.NVarChar, 100).Value  = (object?)NullIfEmpty(Form.NumRequisicion) ?? DBNull.Value;
        cmd.Parameters.Add("@reviso",     SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Form.Reviso)         ?? DBNull.Value;
        cmd.Parameters.Add("@aprobo",     SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Form.Aprobo)         ?? DBNull.Value;

        try
        {
            var newId = (int)(await cmd.ExecuteScalarAsync())!;
            if (!string.IsNullOrWhiteSpace(Form.ItemsJson))
            {
                var items = JsonSerializer.Deserialize<List<OcItemDto>>(Form.ItemsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null && items.Count > 0)
                    await InsertDetalleAsync(conn, newId, items);
            }
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", $"Error al guardar: {ex.Message}");
            await Task.WhenAll(CargarUltimasAsync(), CargarProveedoresAsync(), CargarEmpleadosAsync(), CargarProyectosAsync());
            return Page();
        }

        return RedirectToPage("/OrdenesCompra", new { saved = 1 });
    }

    // ── Editar OC existente ──
    public async Task<IActionResult> OnPostEditarAsync()
    {
        var deny = VerificarAcceso("compras");
        if (deny != null) return deny;

        if (!int.TryParse(Request.Form["EditId"], out int editId))
            return RedirectToPage("/OrdenesCompra");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"UPDATE administracion_proyectos.ordenes_compra
                    SET oc=@oc, proveedor=@proveedor, elaboro=@elaboro, fecha=@fecha,
                        proyecto=@proyecto, cotizacion=@cotizacion, num_requisicion=@numReq,
                        reviso=@reviso, aprobo=@aprobo
                    WHERE id=@id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@id",         SqlDbType.Int).Value            = editId;
        cmd.Parameters.Add("@oc",         SqlDbType.NVarChar, 100).Value  = (object?)NullIfEmpty(Request.Form["EditOc"])             ?? DBNull.Value;
        cmd.Parameters.Add("@proveedor",  SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Request.Form["EditProveedor"])      ?? DBNull.Value;
        cmd.Parameters.Add("@elaboro",    SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Request.Form["EditElaboro"])        ?? DBNull.Value;
        cmd.Parameters.Add("@fecha",      SqlDbType.Date).Value           = DateTime.TryParse(Request.Form["EditFecha"], out var fd) ? fd.Date : DBNull.Value;
        cmd.Parameters.Add("@proyecto",   SqlDbType.NVarChar, -1).Value   = (object?)NullIfEmpty(Request.Form["EditProyecto"])       ?? DBNull.Value;
        cmd.Parameters.Add("@cotizacion", SqlDbType.NVarChar, -1).Value   = (object?)NullIfEmpty(Request.Form["EditCotizacion"])     ?? DBNull.Value;
        cmd.Parameters.Add("@numReq",     SqlDbType.NVarChar, 100).Value  = (object?)NullIfEmpty(Request.Form["EditNumRequisicion"]) ?? DBNull.Value;
        cmd.Parameters.Add("@reviso",     SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Request.Form["EditReviso"])         ?? DBNull.Value;
        cmd.Parameters.Add("@aprobo",     SqlDbType.NVarChar, 200).Value  = (object?)NullIfEmpty(Request.Form["EditAprobo"])         ?? DBNull.Value;

        try
        {
            await cmd.ExecuteNonQueryAsync();

            // Reemplazar detalle: borrar + reinsertar
            var sqlDel = "DELETE FROM administracion_proyectos.ordenes_compra_detalle WHERE id_oc = @id";
            await using var cmdDel = new SqlCommand(sqlDel, conn);
            cmdDel.Parameters.AddWithValue("@id", editId);
            await cmdDel.ExecuteNonQueryAsync();

            var itemsJson = Request.Form["EditItemsJson"].ToString();
            if (!string.IsNullOrWhiteSpace(itemsJson))
            {
                var items = JsonSerializer.Deserialize<List<OcItemDto>>(itemsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null && items.Count > 0)
                    await InsertDetalleAsync(conn, editId, items);
            }
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError("", $"Error al editar: {ex.Message}");
            await CargarUltimasAsync();
            return Page();
        }

        return RedirectToPage("/OrdenesCompra", new { saved = 2 });
    }

    private async Task InsertDetalleAsync(SqlConnection conn, int idOc, List<OcItemDto> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var it  = items[i];
            var sql = @"INSERT INTO administracion_proyectos.ordenes_compra_detalle
                            (id_oc, no_articulo, cod_material, descripcion, cantidad, tipo_unidad, precio_unitario, importe_total)
                        VALUES (@idOc, @no, @cod, @desc, @cant, @um, @pu, @imp)";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@idOc", SqlDbType.Int).Value           = idOc;
            cmd.Parameters.Add("@no",   SqlDbType.Int).Value           = i + 1;
            cmd.Parameters.Add("@cod",  SqlDbType.NVarChar, 100).Value = (object?)NullIfEmpty(it.CodMaterial)  ?? DBNull.Value;
            cmd.Parameters.Add("@desc", SqlDbType.NVarChar, 500).Value = (object?)NullIfEmpty(it.Descripcion)  ?? DBNull.Value;
            cmd.Parameters.Add("@cant", SqlDbType.Decimal).Value       = (object?)it.Cantidad                  ?? DBNull.Value;
            cmd.Parameters.Add("@um",   SqlDbType.NVarChar, 50).Value  = (object?)NullIfEmpty(it.TipoUnidad)   ?? DBNull.Value;
            cmd.Parameters.Add("@pu",   SqlDbType.Decimal).Value       = (object?)it.PrecioUnitario            ?? DBNull.Value;
            cmd.Parameters.Add("@imp",  SqlDbType.Decimal).Value       = (object?)it.ImporteTotal              ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // Detecta si una columna existe en la tabla
    private async Task<bool> ColumnaExisteAsync(SqlConnection conn, string tabla, string columna)
    {
        var sql = "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@tabla) AND name = @col";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        cmd.Parameters.AddWithValue("@col",   columna);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    // Detecta si una tabla existe
    private async Task<bool> TablaExisteAsync(SqlConnection conn, string tabla)
    {
        var sql = "SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@tabla) AND type = 'U'";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task CargarProveedoresAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

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
            while (await rdr.ReadAsync())
                Proveedores.Add(rdr.GetString(0).Trim());
        }
        catch { /* tabla no accesible, se muestra campo vacío */ }
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

    private async Task CargarEmpleadosAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = @"SELECT LTRIM(RTRIM(ISNULL(Nombre,'') + ' ' + ISNULL(ApellidoPaterno,''))) AS nombre_completo
                        FROM inventario.cat_empleados
                        ORDER BY ApellidoPaterno, Nombre";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var nombre = rdr.GetString(0).Trim();
                if (!string.IsNullOrEmpty(nombre))
                    Empleados.Add(nombre);
            }
        }
        catch { /* tabla no accesible, se muestra campo vacío */ }
    }

    private async Task CargarUltimasAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        bool tieneNumReq = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "num_requisicion");
        bool tieneRevC   = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "reviso");
        bool tieneAprC   = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "aprobo");
        bool tieneEstado = await ColumnaExisteAsync(conn, "administracion_proyectos.ordenes_compra", "estado");

        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(FiltOc))         clauses.Add("oc         LIKE '%'+@fOc+'%'");
        if (!string.IsNullOrEmpty(FiltProveedor))  clauses.Add("proveedor  LIKE '%'+@fProv+'%'");
        if (!string.IsNullOrEmpty(FiltElaboro))    clauses.Add("elaboro    LIKE '%'+@fElab+'%'");
        if (!string.IsNullOrEmpty(FiltFecha))      clauses.Add("CONVERT(date, fecha) = CAST(@fFecha AS date)");
        if (!string.IsNullOrEmpty(FiltProyecto))   clauses.Add("proyecto   LIKE '%'+@fProy+'%'");
        if (!string.IsNullOrEmpty(FiltCotizacion)) clauses.Add("cotizacion LIKE '%'+@fCot+'%'");
        string wc = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        string numReqCol  = tieneNumReq ? ", ISNULL(num_requisicion,'') AS num_requisicion" : ", '' AS num_requisicion";
        string revisoCol  = tieneRevC   ? ", ISNULL(reviso,'') AS reviso"                   : ", '' AS reviso";
        string aproboCol  = tieneAprC   ? ", ISNULL(aprobo,'') AS aprobo"                   : ", '' AS aprobo";
        string estadoCol  = tieneEstado ? ", ISNULL(estado,'falta_pago') AS estado"         : ", 'falta_pago' AS estado";
        var sql = $@"
SELECT id, oc, proveedor, elaboro, fecha, proyecto, cotizacion{numReqCol}{revisoCol}{aproboCol}{estadoCol}
FROM administracion_proyectos.ordenes_compra
{wc}
ORDER BY id DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(FiltOc))         cmd.Parameters.Add("@fOc",    SqlDbType.NVarChar, 300).Value = FiltOc.Trim();
        if (!string.IsNullOrEmpty(FiltProveedor))  cmd.Parameters.Add("@fProv",  SqlDbType.NVarChar, 300).Value = FiltProveedor.Trim();
        if (!string.IsNullOrEmpty(FiltElaboro))    cmd.Parameters.Add("@fElab",  SqlDbType.NVarChar, 300).Value = FiltElaboro.Trim();
        if (!string.IsNullOrEmpty(FiltFecha))      cmd.Parameters.Add("@fFecha", SqlDbType.NVarChar, 20).Value  = FiltFecha.Trim();
        if (!string.IsNullOrEmpty(FiltProyecto))   cmd.Parameters.Add("@fProy",  SqlDbType.NVarChar, 300).Value = FiltProyecto.Trim();
        if (!string.IsNullOrEmpty(FiltCotizacion)) cmd.Parameters.Add("@fCot",   SqlDbType.NVarChar, 300).Value = FiltCotizacion.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        Ultimas.Clear();
        while (await rdr.ReadAsync())
        {
            Ultimas.Add(new OrdenCompraRow
            {
                Id             = rdr.GetInt32(0),
                Oc             = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                Proveedor      = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                Elaboro        = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                Fecha          = rdr.IsDBNull(4) ? null : rdr.GetDateTime(4),
                Proyecto       = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                Cotizacion     = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                NumRequisicion = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                Reviso         = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                Aprobo         = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                Estado         = rdr.IsDBNull(10) ? "falta_pago" : rdr.GetString(10),
            });
        }
        TotalRecords = Ultimas.Count;
    }

    // ── Cambiar estado de una OC (llamada AJAX) ──
    public async Task<IActionResult> OnGetCambiarEstadoAsync(int id, string estado)
    {
        var deny = VerificarAccesoJson("compras");
        if (deny != null) return deny;

        var permitidos = new[] { "pagado", "falta_pago", "cancelado" };
        if (!permitidos.Contains(estado))
            return new JsonResult(new { ok = false });
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var sql = "UPDATE administracion_proyectos.ordenes_compra SET estado = @estado WHERE id = @id";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@estado", SqlDbType.NVarChar, 20).Value = estado;
            cmd.Parameters.Add("@id",     SqlDbType.Int).Value           = id;
            await cmd.ExecuteNonQueryAsync();
            return new JsonResult(new { ok = true });
        }
        catch { return new JsonResult(new { ok = false }); }
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class OrdenCompraForm
    {
        public string?   Oc             { get; set; }
        public string?   Proveedor      { get; set; }
        public string?   Elaboro        { get; set; }
        public DateTime? Fecha          { get; set; } = DateTime.Today;
        public string?   Proyecto       { get; set; }
        public string?   Cotizacion     { get; set; }
        public string?   NumRequisicion { get; set; }
        public string?   Reviso         { get; set; }
        public string?   Aprobo         { get; set; }
        public string?   ItemsJson      { get; set; }  // JSON de líneas de detalle
    }

    public class OcItemDto
    {
        public string?  CodMaterial    { get; set; }
        public string?  Descripcion    { get; set; }
        public decimal? Cantidad       { get; set; }
        public string?  TipoUnidad     { get; set; }
        public decimal? PrecioUnitario { get; set; }
        public decimal? ImporteTotal   { get; set; }
    }

    public class OrdenCompraRow
    {
        public int       Id             { get; set; }
        public string    Oc             { get; set; } = "";
        public string?   Proveedor      { get; set; }
        public string?   Elaboro        { get; set; }
        public DateTime? Fecha          { get; set; }
        public string?   Proyecto       { get; set; }
        public string?   Cotizacion     { get; set; }
        public string?   NumRequisicion { get; set; }
        public string?   Reviso         { get; set; }
        public string?   Aprobo         { get; set; }
        public string    Estado         { get; set; } = "falta_pago";
    }
}
