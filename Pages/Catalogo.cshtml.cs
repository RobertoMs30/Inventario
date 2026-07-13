using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class CatalogoModel : PageBase
{
    private readonly IConfiguration _config;

    public CatalogoModel(IConfiguration config)
    {
        _config = config;
    }

    public List<MaterialRow> Materiales { get; private set; } = new();

    // Búsqueda
    public string? Q { get; private set; }
    public string? Sort { get; private set; }

    // Paginación
    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 25;
    public int TotalRecords { get; private set; }
    public int TotalPages => TotalRecords > 0 ? (TotalRecords + PageSize - 1) / PageSize : 1;

    // Alertas
    public int MaterialesNegativos { get; private set; }
    public List<string> CodigosNegativos { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string? q, string? sort, string? deleted, int p = 1)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        var connStr = _config.GetConnectionString("SqlServer");

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Verificar materiales con stock negativo
        var sqlNeg = "SELECT cod_fab FROM inventario.catalogo_materiales WHERE cant < 0 AND cod_fab != 'ND' ORDER BY cant ASC";
        await using (var cmdNeg = new SqlCommand(sqlNeg, conn))
        await using (var rdrNeg = await cmdNeg.ExecuteReaderAsync())
        {
            while (await rdrNeg.ReadAsync())
                CodigosNegativos.Add(rdrNeg.IsDBNull(0) ? "" : rdrNeg.GetString(0));
        }
        MaterialesNegativos = CodigosNegativos.Count;

        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        Q = q;
        sort = string.IsNullOrWhiteSpace(sort) ? "recientes" : sort.Trim().ToLower();
        Sort = sort;

        // Validar página
        if (p < 1) p = 1;
        CurrentPage = p;

        int offset = (p - 1) * PageSize;

        // 1) Contar total de registros
        var sqlCount = @"
SELECT COUNT(*)
FROM inventario.catalogo_materiales
WHERE
    (@q IS NULL OR @q = ''
     OR cod_fab LIKE '%' + @q + '%'
     OR no_part LIKE '%' + @q + '%'
     OR descripcion LIKE '%' + @q + '%'
     OR um LIKE '%' + @q + '%'
     OR LTRIM(RTRIM(partida)) LIKE '%' + @q + '%'
     OR ISNULL(proveedor,'') LIKE '%' + @q + '%'
     OR ISNULL(moneda,'') LIKE '%' + @q + '%'
     OR CONVERT(varchar(50), ISNULL(pu, 0)) LIKE '%' + @q + '%'
     OR CONVERT(varchar(50), ISNULL(cant, 0)) LIKE '%' + @q + '%'
    )";

        await using (var cmdCount = new SqlCommand(sqlCount, conn))
        {
            cmdCount.Parameters.Add("@q",    SqlDbType.NVarChar, 200).Value = (object?)q    ?? DBNull.Value;
            cmdCount.Parameters.Add("@sort", SqlDbType.NVarChar, 20).Value  = (object?)sort ?? DBNull.Value;
            var result = await cmdCount.ExecuteScalarAsync();
            TotalRecords = Convert.ToInt32(result);
        }

        // Ajustar página si excede el total
        if (CurrentPage > TotalPages && TotalPages > 0)
        {
            CurrentPage = TotalPages;
            offset = (CurrentPage - 1) * PageSize;
        }

        // 2) Obtener registros con paginación
        var sql = @"
SELECT
    id,
    marca,
    cod_fab,
    no_part,
    descripcion,
    um,
    cant,
    LTRIM(RTRIM(partida)) AS partida,
    pu,
    proveedor,
    moneda
FROM inventario.catalogo_materiales
WHERE
    (@q IS NULL OR @q = ''
     OR cod_fab LIKE '%' + @q + '%'
     OR no_part LIKE '%' + @q + '%'
     OR descripcion LIKE '%' + @q + '%'
     OR um LIKE '%' + @q + '%'
     OR LTRIM(RTRIM(partida)) LIKE '%' + @q + '%'
     OR ISNULL(proveedor,'') LIKE '%' + @q + '%'
     OR ISNULL(moneda,'') LIKE '%' + @q + '%'
     OR CONVERT(varchar(50), ISNULL(pu, 0)) LIKE '%' + @q + '%'
     OR CONVERT(varchar(50), ISNULL(cant, 0)) LIKE '%' + @q + '%'
    )
ORDER BY
    CASE WHEN @sort = 'recientes' THEN id  END DESC,
    CASE WHEN @sort = 'cantidad'  THEN cant END DESC,
    CASE WHEN @sort = 'precio'    THEN pu   END DESC,
    CASE WHEN @sort = 'az' OR @sort IS NULL THEN cod_fab END ASC
OFFSET @offset ROWS
FETCH NEXT @pageSize ROWS ONLY;
";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q",       SqlDbType.NVarChar, 200).Value = (object?)q    ?? DBNull.Value;
        cmd.Parameters.Add("@sort",    SqlDbType.NVarChar, 20).Value  = (object?)sort ?? DBNull.Value;
        cmd.Parameters.Add("@offset",  SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@pageSize",SqlDbType.Int).Value = PageSize;

        await using var rdr = await cmd.ExecuteReaderAsync();

        var ordId         = rdr.GetOrdinal("id");
        var ordMarca      = rdr.GetOrdinal("marca");
        var ordCodFab     = rdr.GetOrdinal("cod_fab");
        var ordNoPart     = rdr.GetOrdinal("no_part");
        var ordDescripcion= rdr.GetOrdinal("descripcion");
        var ordUm         = rdr.GetOrdinal("um");
        var ordCant       = rdr.GetOrdinal("cant");
        var ordPartida    = rdr.GetOrdinal("partida");
        var ordPu         = rdr.GetOrdinal("pu");
        var ordProveedor  = rdr.GetOrdinal("proveedor");
        var ordMoneda     = rdr.GetOrdinal("moneda");

        Materiales.Clear();

        while (await rdr.ReadAsync())
        {
            Materiales.Add(new MaterialRow
            {
                Id          = rdr.GetInt32(ordId),
                Marca       = rdr.IsDBNull(ordMarca) ? null : rdr.GetString(ordMarca),
                CodFab      = rdr.IsDBNull(ordCodFab) ? "" : rdr.GetString(ordCodFab),
                NoPart      = rdr.IsDBNull(ordNoPart) ? null : rdr.GetString(ordNoPart),
                Descripcion = rdr.IsDBNull(ordDescripcion) ? "" : rdr.GetString(ordDescripcion),
                Um          = rdr.IsDBNull(ordUm) ? "" : rdr.GetString(ordUm),
                Cant        = rdr.IsDBNull(ordCant) ? (decimal?)null : rdr.GetDecimal(ordCant),
                Partida     = rdr.IsDBNull(ordPartida) ? null : rdr.GetString(ordPartida),
                Pu          = rdr.IsDBNull(ordPu) ? (decimal?)null : rdr.GetDecimal(ordPu),
                Proveedor   = rdr.IsDBNull(ordProveedor) ? null : rdr.GetString(ordProveedor),
                Moneda      = rdr.IsDBNull(ordMoneda) ? null : rdr.GetString(ordMoneda),
            });
        }

        return Page();
    }

    // ── Editar material completo (campos + cantidad) ─────────────────────────
    // Devuelve el no_po más reciente de entradas_inventario para un cod_fab
    public async Task<IActionResult> OnGetNoPoYFacPorCodFabAsync(string codFab)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(codFab))
            return new JsonResult(new { no_po = "", fac = "" });
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT TOP 1 no_po, fac FROM inventario.entradas_inventario
                    WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab
                    ORDER BY id DESC";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = codFab.Trim();
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
            return new JsonResult(new {
                no_po = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                fac   = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
            });
        return new JsonResult(new { no_po = "", fac = "" });
    }

    // Devuelve el fac más reciente de entradas_inventario para un cod_fab
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

    public async Task<IActionResult> OnGetFacPorCodFabAsync(string codFab)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(codFab))
            return new JsonResult(new { fac = "" });
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT TOP 1 fac FROM inventario.entradas_inventario
                    WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab AND fac IS NOT NULL AND fac != ''
                    ORDER BY id DESC";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = codFab.Trim();
        var result = await cmd.ExecuteScalarAsync();
        return new JsonResult(new { fac = result?.ToString() ?? "" });
    }

    public async Task<IActionResult> OnPostGuardarEdicionAsync(
        int rowId, string? marca, string codFab, string descripcion, string um,
        string proveedor, string partida, decimal? pu, string moneda,
        string? cantStr, string? fac, string? noPo, string? proyecto, string? q, int p = 1)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (rowId <= 0)
            return RedirectToPage();

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Parsear cantidad
        decimal? cantVal = null;
        if (!string.IsNullOrWhiteSpace(cantStr) &&
            decimal.TryParse(cantStr,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal parsedCant))
            cantVal = parsedCant;

        // Actualizar todos los campos usando el id como llave única
        // ISNULL(@codFab, cod_fab) preserva el valor existente si no se envía uno nuevo
        var sql = @"
UPDATE inventario.catalogo_materiales SET
    marca       = @marca,
    cod_fab     = ISNULL(NULLIF(LTRIM(RTRIM(@codFab)), ''), cod_fab),
    descripcion = @descripcion,
    um          = @um,
    proveedor   = @proveedor,
    partida     = @partida,
    pu          = @pu,
    moneda      = @moneda"
    + (cantVal.HasValue ? ",\n    cant = @cant" : "") + @"
WHERE id = @id;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@marca",       SqlDbType.NVarChar, 100).Value = (object?)(marca?.Trim()) ?? DBNull.Value;
        cmd.Parameters.Add("@codFab",      SqlDbType.NVarChar, 200).Value = (object?)(codFab?.Trim()) ?? DBNull.Value;
        cmd.Parameters.Add("@descripcion", SqlDbType.NVarChar, -1 ).Value = (descripcion ?? "").Trim();
        cmd.Parameters.Add("@um",          SqlDbType.NVarChar, 50 ).Value = (um ?? "").Trim();
        cmd.Parameters.Add("@proveedor",   SqlDbType.NVarChar, 200).Value = (object?)(proveedor?.Trim()) ?? DBNull.Value;
        cmd.Parameters.Add("@partida",     SqlDbType.NVarChar, 200).Value = (object?)(partida?.Trim())   ?? DBNull.Value;
        cmd.Parameters.Add("@pu",          SqlDbType.Decimal       ).Value = (object?)pu ?? DBNull.Value;
        cmd.Parameters.Add("@moneda",      SqlDbType.NVarChar, 10 ).Value = (moneda ?? "MXN").Trim();
        cmd.Parameters.Add("@id",          SqlDbType.Int           ).Value = rowId;
        if (cantVal.HasValue)
            cmd.Parameters.Add("@cant", SqlDbType.Decimal).Value = cantVal.Value;

        await cmd.ExecuteNonQueryAsync();

        // Actualizar fac, no_po y proyecto solo en la entrada más reciente del material
        if (!string.IsNullOrWhiteSpace(fac) || !string.IsNullOrWhiteSpace(noPo) || !string.IsNullOrWhiteSpace(proyecto))
        {
            var sets = new List<string>();
            if (!string.IsNullOrWhiteSpace(fac))      sets.Add("fac = @fac");
            if (!string.IsNullOrWhiteSpace(noPo))     sets.Add("no_po = @no_po");
            if (!string.IsNullOrWhiteSpace(proyecto)) sets.Add("proyecto = @proyecto");
            var sqlUpd = $"UPDATE inventario.entradas_inventario SET {string.Join(", ", sets)} WHERE id = (SELECT MAX(id) FROM inventario.entradas_inventario WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab)";
            await using var cmdUpd = new SqlCommand(sqlUpd, conn);
            if (!string.IsNullOrWhiteSpace(fac))      cmdUpd.Parameters.Add("@fac",      SqlDbType.NVarChar, 100).Value = fac.Trim();
            if (!string.IsNullOrWhiteSpace(noPo))     cmdUpd.Parameters.Add("@no_po",    SqlDbType.NVarChar, 100).Value = noPo.Trim();
            if (!string.IsNullOrWhiteSpace(proyecto)) cmdUpd.Parameters.Add("@proyecto", SqlDbType.NVarChar, 100).Value = proyecto.Trim();
            cmdUpd.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = codFab.Trim();
            await cmdUpd.ExecuteNonQueryAsync();
        }

        var qs = string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}";
        return Redirect($"/Catalogo?saved=1&p={p}{qs}");
    }

    // Método POST para eliminar material
    public async Task<IActionResult> OnPostEliminarAsync(string codFab)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (string.IsNullOrWhiteSpace(codFab))
            return RedirectToPage();

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Eliminar de catalogo_materiales
        var sqlCatalogo = @"
            DELETE FROM inventario.catalogo_materiales
            WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab";

        await using var cmdCatalogo = new SqlCommand(sqlCatalogo, conn);
        cmdCatalogo.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = codFab.Trim();
        await cmdCatalogo.ExecuteNonQueryAsync();

        // También eliminar las entradas del inventario asociadas
        var sqlEntradas = @"
            DELETE FROM inventario.entradas_inventario
            WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab";

        await using var cmdEntradas = new SqlCommand(sqlEntradas, conn);
        cmdEntradas.Parameters.Add("@cod_fab", SqlDbType.NVarChar, 100).Value = codFab.Trim();
        await cmdEntradas.ExecuteNonQueryAsync();

        return RedirectToPage(new { deleted = 1 });
    }

    public class MaterialRow
    {
        public int    Id     { get; set; }
        public string? Marca { get; set; }
        public string CodFab { get; set; } = "";
        public string? NoPart { get; set; }
        public string Descripcion { get; set; } = "";
        public string Um { get; set; } = "";
        public decimal? Cant { get; set; }

        public string? Partida { get; set; }
        public decimal? Pu { get; set; }
        public string? Proveedor { get; set; }
        public string? Moneda { get; set; }
    }
}
