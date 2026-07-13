using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class EntradaDesdeCotizacionModel : PageBase
{
    private readonly IConfiguration _config;

    public EntradaDesdeCotizacionModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public int CotizacionId { get; set; }

    public CotizacionInfo? Cotizacion { get; private set; }
    public List<ItemEntrada> Items { get; private set; } = new();

    // Primer cod_int disponible al cargar la página (orientativo en UI)
    public string PrimerCodInt { get; private set; } = "";

    public string? ErrorMessage { get; private set; }
    public string? SuccessMessage { get; private set; }

    // ── GET ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (CotizacionId <= 0)
            return RedirectToPage("/Cotizaciones");

        await CargarCotizacionAsync();

        if (Cotizacion is null)
            return RedirectToPage("/Cotizaciones");

        PrimerCodInt = await GenerarCodIntAsync();
        return Page();
    }

    // ── Autocomplete: buscar en catálogo ─────────────────────────
    public async Task<JsonResult> OnGetBuscarCatalogoAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return (JsonResult)deny;

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 10
                c.cod_fab,
                ISNULL(c.descripcion, '') AS descripcion,
                ISNULL(c.um,          '') AS um,
                ISNULL(c.proveedor,   '') AS proveedor,
                ISNULL(c.pu,           0) AS pu,
                ISNULL(c.moneda,    'MXN') AS moneda,
                ISNULL(c.partida,   '')   AS partida,
                CASE WHEN EXISTS (
                    SELECT 1 FROM inventario.catalogo_materiales
                    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(c.cod_fab))
                ) THEN 1 ELSE 0 END AS existe
            FROM inventario.catalogo_materiales c
            WHERE c.cod_fab     LIKE '%' + @q + '%'
               OR c.descripcion LIKE '%' + @q + '%'
               OR c.no_part     LIKE '%' + @q + '%'
            ORDER BY c.cod_fab;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            results.Add(new
            {
                codFab      = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                descripcion = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                um          = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                proveedor   = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                pu          = rdr.IsDBNull(4) ? 0m : rdr.GetDecimal(4),
                moneda      = rdr.IsDBNull(5) ? "MXN" : rdr.GetString(5),
                partida     = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                existe      = rdr.GetInt32(7) == 1,
            });
        }
        return new JsonResult(results);
    }

    // ── POST ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAsync(
        [FromForm] List<ItemPostForm> items,
        [FromForm] string? fechaGlobal,
        [FromForm] string? noPoGlobal,
        [FromForm] string? facGlobal)
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (CotizacionId <= 0)
            return RedirectToPage("/Cotizaciones");

        // Re-cargar cabecera para mostrar si hay error
        await CargarCotizacionAsync();
        PrimerCodInt = await GenerarCodIntAsync();

        var seleccionados = items.Where(i => i.Seleccionado).ToList();

        if (seleccionados.Count == 0)
        {
            ErrorMessage = "Debes seleccionar al menos un ítem para dar entrada.";
            return Page();
        }

        // Validar que cada seleccionado tenga CodFab
        var errores = new List<string>();
        for (int i = 0; i < seleccionados.Count; i++)
        {
            var it = seleccionados[i];
            if (string.IsNullOrWhiteSpace(it.CodFab))
                errores.Add($"Ítem \"{it.Descripcion?.Truncate(40)}\": el Cod. Fab. es obligatorio.");
            if (it.Cantidad <= 0)
                errores.Add($"Ítem \"{it.Descripcion?.Truncate(40)}\": la cantidad debe ser mayor a 0.");
        }

        // Fecha: usar la global si no viene por ítem
        DateTime fechaBase = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(fechaGlobal) && DateTime.TryParse(fechaGlobal, out var fg))
            fechaBase = fg.Date;

        if (errores.Count > 0)
        {
            ErrorMessage = string.Join(" | ", errores);
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");

        // Generar cod_int base y asignar uno por ítem
        long codIntBase = await GenerarCodIntNumericoAsync();

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int registrados = 0;
        try
        {
            for (int i = 0; i < seleccionados.Count; i++)
            {
                var it = seleccionados[i];
                string codInt = (codIntBase + i).ToString();

                // Verificar existencia en catálogo dentro de la misma transacción
                bool existe;
                var sqlEx = @"SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM inventario.catalogo_materiales
                    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cf))
                ) THEN 1 ELSE 0 END;";
                await using (var cmdEx = new SqlCommand(sqlEx, conn, (SqlTransaction)tx))
                {
                    cmdEx.Parameters.AddWithValue("@cf", it.CodFab!.Trim());
                    var r = await cmdEx.ExecuteScalarAsync();
                    existe = Convert.ToInt32(r) == 1;
                }

                // Si es nuevo, validar campos extra
                if (!existe)
                {
                    if (string.IsNullOrWhiteSpace(it.Partida))
                        it.Partida = "GENERAL";
                    if (it.Pu is null || it.Pu <= 0)
                        it.Pu = it.PrecioUnitarioCot > 0 ? it.PrecioUnitarioCot : 0;
                    if (string.IsNullOrWhiteSpace(it.Moneda))
                        it.Moneda = it.MonedaCot ?? "MXN";
                }

                DateTime fechaEntrada = fechaBase;
                if (!string.IsNullOrWhiteSpace(it.FechaCompra) && DateTime.TryParse(it.FechaCompra, out var fe))
                    fechaEntrada = fe.Date;

                string? noPo = !string.IsNullOrWhiteSpace(it.NoPo) ? it.NoPo.Trim()
                             : !string.IsNullOrWhiteSpace(noPoGlobal) ? noPoGlobal.Trim()
                             : null;

                string? fac  = !string.IsNullOrWhiteSpace(it.Fac) ? it.Fac.Trim()
                             : !string.IsNullOrWhiteSpace(facGlobal) ? facGlobal.Trim()
                             : null;

                await using var cmd = new SqlCommand("dbo.sp_RegistrarEntradaMaterial", conn, (SqlTransaction)tx);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@cod_fab",      it.CodFab!.Trim());
                cmd.Parameters.AddWithValue("@cod_int",      codInt);
                cmd.Parameters.AddWithValue("@descripcion",  (it.Descripcion ?? "").Trim());
                cmd.Parameters.AddWithValue("@cantidad",     it.Cantidad);
                cmd.Parameters.AddWithValue("@um",           (it.Unidad ?? "").Trim());
                cmd.Parameters.AddWithValue("@fecha_compra", fechaEntrada);
                cmd.Parameters.AddWithValue("@proveedor",    (it.Proveedor ?? "").Trim());
                cmd.Parameters.AddWithValue("@no_po",        (object?)noPo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fac",          (object?)fac  ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@diferencia",   DBNull.Value);
                cmd.Parameters.AddWithValue("@proyecto",     (object?)NullIfEmpty(Cotizacion?.Proyecto) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@no_part",      DBNull.Value);
                cmd.Parameters.AddWithValue("@partida",      (object?)NullIfEmpty(it.Partida) ?? DBNull.Value);

                var pPu = cmd.Parameters.Add("@pu", SqlDbType.Decimal);
                pPu.Precision = 18; pPu.Scale = 2;
                pPu.Value = !existe && it.Pu.HasValue ? (object)it.Pu.Value : DBNull.Value;

                cmd.Parameters.Add("@moneda", SqlDbType.NVarChar, 50).Value =
                    !existe && !string.IsNullOrWhiteSpace(it.Moneda) ? (object)it.Moneda : DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
                registrados++;
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            ErrorMessage = $"Error al guardar: ningún ítem fue registrado. Intenta de nuevo. ({ex.Message})";
            return Page();
        }

        return RedirectToPage("/EntradaDesdeCotizacion",
            new { cotizacionId = CotizacionId, ok = registrados });
    }

    // ── Helpers ──────────────────────────────────────────────────
    private async Task CargarCotizacionAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_ObtenerCotizacionDetalle", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = CotizacionId;

        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync()) return;

        Cotizacion = new CotizacionInfo
        {
            Id       = rdr.GetInt32(rdr.GetOrdinal("id")),
            Folio    = rdr.GetString(rdr.GetOrdinal("folio")),
            Fecha    = rdr.GetDateTime(rdr.GetOrdinal("fecha")),
            Cliente  = rdr.GetString(rdr.GetOrdinal("cliente")),
            Proyecto = rdr.GetString(rdr.GetOrdinal("proyecto")),
        };

        if (!await rdr.NextResultAsync()) return;

        var ordId    = rdr.GetOrdinal("id");
        var ordNo    = rdr.GetOrdinal("no_item");
        var ordDesc  = rdr.GetOrdinal("descripcion");
        var ordCant  = rdr.GetOrdinal("cantidad");
        var ordUm    = rdr.GetOrdinal("unidad");
        var ordMarca = rdr.GetOrdinal("marca");
        var ordPu    = rdr.GetOrdinal("precio_unitario");
        var ordMon   = rdr.GetOrdinal("moneda");

        while (await rdr.ReadAsync())
        {
            Items.Add(new ItemEntrada
            {
                ItemId      = rdr.GetInt32(ordId),
                NoItem      = rdr.GetInt32(ordNo),
                Descripcion = rdr.GetString(ordDesc),
                Cantidad    = rdr.GetDecimal(ordCant),
                Unidad      = rdr.GetString(ordUm),
                Proveedor   = "",
                PrecioUnitarioCot = rdr.GetDecimal(ordPu),
                MonedaCot   = rdr.GetString(ordMon),
            });
        }
    }

    private async Task<string> GenerarCodIntAsync()
        => (await GenerarCodIntNumericoAsync()).ToString();

    private async Task<long> GenerarCodIntNumericoAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT MAX(v) + 1 FROM (
                        SELECT ISNULL(MAX(TRY_CAST(cod_int AS BIGINT)), 0) AS v
                        FROM inventario.entradas_inventario
                        UNION ALL SELECT 3005204
                    ) AS t";
        await using var cmd = new SqlCommand(sql, conn);
        var r = await cmd.ExecuteScalarAsync();
        return r == null || r == DBNull.Value ? 3005205L : Convert.ToInt64(r);
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── DTOs ────────────────────────────────────────────────────
    public class CotizacionInfo
    {
        public int      Id       { get; set; }
        public string   Folio    { get; set; } = "";
        public DateTime Fecha    { get; set; }
        public string   Cliente  { get; set; } = "";
        public string   Proyecto { get; set; } = "";
    }

    public class ItemEntrada
    {
        public int     ItemId            { get; set; }
        public int     NoItem            { get; set; }
        public string  Descripcion       { get; set; } = "";
        public decimal Cantidad          { get; set; }
        public string  Unidad            { get; set; } = "";
        public string  Proveedor         { get; set; } = "";
        public decimal PrecioUnitarioCot { get; set; }
        public string  MonedaCot         { get; set; } = "MXN";
    }

    public class ItemPostForm
    {
        public bool    Seleccionado      { get; set; }
        public string? CodFab            { get; set; }
        public string? Descripcion       { get; set; }
        public decimal Cantidad          { get; set; }
        public string? Unidad            { get; set; }
        public string? Proveedor         { get; set; }
        public string? FechaCompra       { get; set; }
        public string? NoPo              { get; set; }
        public string? Fac               { get; set; }
        // Campos para material nuevo en catálogo
        public string?  Partida          { get; set; }
        public decimal? Pu               { get; set; }
        public string?  Moneda           { get; set; }
        // Datos originales de la cotización (para fallback)
        public decimal  PrecioUnitarioCot { get; set; }
        public string?  MonedaCot         { get; set; }
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
