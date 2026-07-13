using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace InventarioWeb.Pages;

public class EditarCotizacionModel : PageBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    public EditarCotizacionModel(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    [BindProperty]
    public EditarCotizacionForm Form { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    // JSON de ítems existentes para pre-llenar la tabla en el cliente
    public string ItemsJson { get; private set; } = "[]";

    // ── GET: carga la cotización existente ──────────────────
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        if (id <= 0)
            return RedirectToPage("/Cotizaciones");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_ObtenerCotizacionDetalle", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;

        await using var rdr = await cmd.ExecuteReaderAsync();

        // Encabezado
        if (!await rdr.ReadAsync())
            return RedirectToPage("/Cotizaciones");

        Form.Id           = id;
        Form.Fecha        = rdr.GetDateTime(rdr.GetOrdinal("fecha"));
        Form.Cliente      = rdr.GetString(rdr.GetOrdinal("cliente"));
        Form.Proyecto     = rdr.GetString(rdr.GetOrdinal("proyecto"));
        var ordRfq  = rdr.GetOrdinal("rfq");
        Form.Rfq    = rdr.IsDBNull(ordRfq) ? null : rdr.GetString(ordRfq);
        Form.DescuentoPct = rdr.GetDecimal(rdr.GetOrdinal("descuento_pct"));
        Form.Formato      = rdr.GetByte(rdr.GetOrdinal("formato"));
        var ordSol  = rdr.GetOrdinal("solicitante");
        var ordResp = rdr.GetOrdinal("responsable");
        var ordLoc  = rdr.GetOrdinal("localidad");
        var ordNoPu = rdr.GetOrdinal("no_puertos");
        var ordSec  = rdr.GetOrdinal("seccion_titulo");
        var ordCond = rdr.GetOrdinal("condiciones_comerciales");
        Form.Solicitante            = rdr.IsDBNull(ordSol)  ? null : rdr.GetString(ordSol);
        Form.Responsable            = rdr.IsDBNull(ordResp) ? null : rdr.GetString(ordResp);
        Form.Localidad              = rdr.IsDBNull(ordLoc)  ? null : rdr.GetString(ordLoc);
        Form.NoPuertos              = rdr.IsDBNull(ordNoPu) ? null : rdr.GetString(ordNoPu);
        Form.SeccionTitulo          = rdr.IsDBNull(ordSec)  ? null : rdr.GetString(ordSec);
        Form.CondicionesComerciales = rdr.IsDBNull(ordCond) ? null : rdr.GetString(ordCond);

        // Ítems
        var items = new List<object>();
        if (await rdr.NextResultAsync())
        {
            var ordNoItem   = rdr.GetOrdinal("no_item");
            var ordDesc     = rdr.GetOrdinal("descripcion");
            var ordCant     = rdr.GetOrdinal("cantidad");
            var ordUm       = rdr.GetOrdinal("unidad");
            var ordMarca    = rdr.GetOrdinal("marca");
            var ordMo       = rdr.GetOrdinal("mano_obra");
            var ordPu       = rdr.GetOrdinal("precio_unitario");
            var ordTotal    = rdr.GetOrdinal("total");
            var ordMoneda   = rdr.GetOrdinal("moneda");
            var ordTe       = rdr.GetOrdinal("tiempo_entrega");
            var ordCostoUsd = rdr.GetOrdinal("costo_usd");
            var ordTc       = rdr.GetOrdinal("tc");
            var ordPctVenta = rdr.GetOrdinal("pct_venta");
            var ordPctMo    = rdr.GetOrdinal("pct_mo");

            while (await rdr.ReadAsync())
            {
                items.Add(new
                {
                    noItem         = rdr.GetInt32(ordNoItem),
                    descripcion    = rdr.GetString(ordDesc),
                    cantidad       = rdr.GetDecimal(ordCant),
                    unidad         = rdr.GetString(ordUm),
                    marca          = rdr.IsDBNull(ordMarca) ? "" : rdr.GetString(ordMarca),
                    manoObra       = rdr.GetDecimal(ordMo),
                    precioUnitario = rdr.GetDecimal(ordPu),
                    total          = rdr.GetDecimal(ordTotal),
                    moneda         = rdr.GetString(ordMoneda),
                    tiempoEntrega  = rdr.IsDBNull(ordTe) ? "" : rdr.GetString(ordTe),
                    costoUsd       = rdr.IsDBNull(ordCostoUsd) ? (decimal?)null : rdr.GetDecimal(ordCostoUsd),
                    tc             = rdr.IsDBNull(ordTc)       ? (decimal?)null : rdr.GetDecimal(ordTc),
                    pctVenta       = rdr.IsDBNull(ordPctVenta) ? (decimal?)null : rdr.GetDecimal(ordPctVenta),
                    pctMo          = rdr.IsDBNull(ordPctMo)    ? (decimal?)null : rdr.GetDecimal(ordPctMo),
                });
            }
        }

        ItemsJson = JsonSerializer.Serialize(items);
        return Page();
    }

    // ── Tipo de cambio USD/MXN desde Banxico ────────────────
    public async Task<IActionResult> OnGetTipoCambioAsync()
    {
        var deny = VerificarAccesoJson("cotizaciones");
        if (deny != null) return deny;
        try
        {
            var token = _config["Banxico:Token"];
            var serie = _config["Banxico:SerieUsdMxn"] ?? "SF43718";
            var url   = $"https://www.banxico.org.mx/SieAPIRest/service/v1/series/{serie}/datos/oportuno";

            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Bmx-Token", token);
            var json = await http.GetStringAsync(url);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var dato = doc.RootElement
                          .GetProperty("bmx")
                          .GetProperty("series")[0]
                          .GetProperty("datos")[0]
                          .GetProperty("dato")
                          .GetString();

            if (decimal.TryParse(dato, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var tc))
                return new JsonResult(new { ok = true, tc });

            return new JsonResult(new { ok = false, error = "Dato no numérico" });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    // ── Listar todas las empresas de administracion_proyectos ──
    public async Task<IActionResult> OnGetListarEmpresasAsync()
    {
        var deny = VerificarAccesoJson("cotizaciones");
        if (deny != null) return deny;
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT razon_social
            FROM administracion_proyectos.tbl_empresas
            WHERE razon_social IS NOT NULL AND razon_social <> ''
            ORDER BY razon_social;";

        await using var cmd = new SqlCommand(sql, conn);
        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            results.Add(new { nombre = rdr.GetString(0) });

        return new JsonResult(results);
    }

    // ── Autocomplete: clientes ───────────────────────────────
    public async Task<IActionResult> OnGetBuscarClienteAsync(string q)
    {
        var deny = VerificarAccesoJson("cotizaciones");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 12 id_cliente, razon_social
            FROM inventario.cat_clientes
            WHERE razon_social LIKE '%' + @q + '%'
              AND (bajalogica IS NULL OR bajalogica = 0)
            ORDER BY razon_social;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            results.Add(new { id = rdr.GetInt32(0), nombre = rdr.IsDBNull(1) ? "" : rdr.GetString(1) });

        return new JsonResult(results);
    }

    // ── Autocomplete: materiales ─────────────────────────────
    public async Task<IActionResult> OnGetBuscarMaterialAsync(string q)
    {
        var deny = VerificarAccesoJson("cotizaciones");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 12
                cod_fab, no_part, descripcion, um,
                ISNULL(proveedor, '') AS proveedor,
                ISNULL(pu, 0)        AS pu,
                ISNULL(moneda,'MXN') AS moneda
            FROM inventario.catalogo_materiales
            WHERE descripcion LIKE '%' + @q + '%'
               OR no_part     LIKE '%' + @q + '%'
               OR cod_fab     LIKE '%' + @q + '%'
            ORDER BY descripcion;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();

        var ordCodFab = rdr.GetOrdinal("cod_fab");
        var ordNoPart = rdr.GetOrdinal("no_part");
        var ordDesc   = rdr.GetOrdinal("descripcion");
        var ordUm     = rdr.GetOrdinal("um");
        var ordProv   = rdr.GetOrdinal("proveedor");
        var ordPu     = rdr.GetOrdinal("pu");
        var ordMoneda = rdr.GetOrdinal("moneda");

        while (await rdr.ReadAsync())
        {
            results.Add(new
            {
                codFab      = rdr.IsDBNull(ordCodFab) ? "" : rdr.GetString(ordCodFab),
                noPart      = rdr.IsDBNull(ordNoPart) ? "" : rdr.GetString(ordNoPart),
                descripcion = rdr.IsDBNull(ordDesc)   ? "" : rdr.GetString(ordDesc),
                um          = rdr.IsDBNull(ordUm)     ? "" : rdr.GetString(ordUm),
                proveedor   = rdr.GetString(ordProv),
                pu          = rdr.GetDecimal(ordPu),
                moneda      = rdr.GetString(ordMoneda),
            });
        }

        return new JsonResult(results);
    }

    // ── POST: guardar cambios ────────────────────────────────
    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        ModelState.Remove     ("Form.Fecha");

        if (Form.Id <= 0)
            return RedirectToPage("/Cotizaciones");

        if (string.IsNullOrWhiteSpace(Form.Cliente))
            ModelState.AddModelError("", "Cliente es obligatorio.");

        if (string.IsNullOrWhiteSpace(Form.Proyecto))
            ModelState.AddModelError("", "Proyecto es obligatorio.");

        if (Form.Fecha is null)
            ModelState.AddModelError("", "Fecha es obligatoria.");

        var realItems = Form.Items?.Where(i => i.Marca?.Trim() != "__PARTIDA__").ToList();
        if (Form.Items == null || Form.Items.Count == 0 || realItems?.Count == 0)
            ModelState.AddModelError("", "Debe agregar al menos un ítem a la cotización.");
        else
        {
            int itemNum = 0;
            for (int i = 0; i < Form.Items.Count; i++)
            {
                var item = Form.Items[i];
                if (item.Marca?.Trim() == "__PARTIDA__") continue;

                itemNum++;
                int noItem = itemNum * 10;

                if (string.IsNullOrWhiteSpace(item.Descripcion))
                    ModelState.AddModelError("", $"Ítem {noItem}: descripción es obligatoria.");

                if (item.Cantidad <= 0)
                    ModelState.AddModelError("", $"Ítem {noItem}: cantidad debe ser mayor a 0.");

                if (string.IsNullOrWhiteSpace(item.Unidad))
                    ModelState.AddModelError("", $"Ítem {noItem}: unidad es obligatoria.");

                if (string.IsNullOrWhiteSpace(item.Moneda))
                    ModelState.AddModelError("", $"Ítem {noItem}: moneda es obligatoria.");
            }
        }

        if (!ModelState.IsValid)
        {
            ErrorMessage = string.Join(" | ",
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            // Re-serializar ítems para mantener la tabla visible
            ItemsJson = JsonSerializer.Serialize(
                Form.Items?.Select((it, idx) => new
                {
                    noItem         = (idx + 1) * 10,
                    descripcion    = it.Descripcion ?? "",
                    cantidad       = it.Cantidad,
                    unidad         = it.Unidad ?? "",
                    marca          = it.Marca ?? "",
                    manoObra       = it.ManoObra,
                    precioUnitario = it.PrecioUnitario,
                    total          = it.Total,
                    moneda         = it.Moneda ?? "MXN",
                    tiempoEntrega  = it.TiempoEntrega ?? "",
                    costoUsd       = it.CostoUsd,
                    tc             = it.Tc,
                    pctVenta       = it.PctVenta,
                    pctMo          = it.PctMo,
                }) ?? Enumerable.Empty<object>()
            );
            return Page();
        }

        var itemsJson = JsonSerializer.Serialize(
            Form.Items!.Select((item, idx) => new
            {
                no_item         = (idx + 1) * 10,
                descripcion     = item.Descripcion?.Trim() ?? "",
                cantidad        = item.Cantidad,
                unidad          = item.Unidad?.Trim() ?? "",
                marca           = item.Marca?.Trim() ?? "",
                mano_obra       = item.ManoObra,
                precio_unitario = item.PrecioUnitario,
                total           = item.Total,
                moneda          = item.Moneda?.Trim() ?? "MXN",
                tiempo_entrega  = item.TiempoEntrega?.Trim() ?? "",
                costo_usd       = item.CostoUsd,
                tc              = item.Tc,
                pct_venta       = item.PctVenta,
                pct_mo          = item.PctMo
            })
        );

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_ActualizarCotizacion", conn);
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add("@id",             SqlDbType.Int).Value          = Form.Id;
        cmd.Parameters.Add("@fecha",          SqlDbType.Date).Value          = Form.Fecha!.Value.Date;
        cmd.Parameters.Add("@cliente",        SqlDbType.NVarChar, 200).Value = Form.Cliente!.Trim();
        cmd.Parameters.Add("@proyecto",       SqlDbType.NVarChar, 300).Value = Form.Proyecto!.Trim();
        cmd.Parameters.Add("@items",          SqlDbType.NVarChar, -1).Value  = itemsJson;
        cmd.Parameters.Add("@rfq",            SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(Form.Rfq) ? (object)DBNull.Value : Form.Rfq.Trim();
        cmd.Parameters.Add("@descuento_pct",  SqlDbType.Decimal).Value       = Form.DescuentoPct;
        cmd.Parameters.Add("@formato",        SqlDbType.TinyInt).Value       = Form.Formato;
        cmd.Parameters.Add("@solicitante",    SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(Form.Solicitante)   ? (object)DBNull.Value : Form.Solicitante.Trim();
        cmd.Parameters.Add("@responsable",   SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(Form.Responsable)   ? (object)DBNull.Value : Form.Responsable.Trim();
        cmd.Parameters.Add("@localidad",      SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(Form.Localidad)     ? (object)DBNull.Value : Form.Localidad.Trim();
        cmd.Parameters.Add("@no_puertos",     SqlDbType.NVarChar, 50).Value  = string.IsNullOrWhiteSpace(Form.NoPuertos)     ? (object)DBNull.Value : Form.NoPuertos.Trim();
        cmd.Parameters.Add("@seccion_titulo",          SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(Form.SeccionTitulo)           ? (object)DBNull.Value : Form.SeccionTitulo.Trim();
        cmd.Parameters.Add("@condiciones_comerciales", SqlDbType.NVarChar, -1).Value  = string.IsNullOrWhiteSpace(Form.CondicionesComerciales) ? (object)DBNull.Value : Form.CondicionesComerciales.Trim();

        await cmd.ExecuteNonQueryAsync();

        return RedirectToPage("/VerCotizacion", new { id = Form.Id, edited = 1 });
    }

    // ─── DTOs ─────────────────────────────────────────────────
    public class EditarCotizacionForm
    {
        public int       Id            { get; set; }
        public DateTime? Fecha         { get; set; }
        public string?   Cliente       { get; set; }
        public string?   Proyecto      { get; set; }
        public string?   Rfq           { get; set; }
        public decimal   DescuentoPct  { get; set; } = 0;
        public byte      Formato       { get; set; } = 1;
        public string?   Solicitante   { get; set; }
        public string?   Responsable   { get; set; }
        public string?   Localidad     { get; set; }
        public string?   NoPuertos     { get; set; }
        public string?   SeccionTitulo           { get; set; }
        public string?   CondicionesComerciales  { get; set; }
        public List<ItemForm> Items              { get; set; } = new();
    }

    public class ItemForm
    {
        public string?  Descripcion     { get; set; }
        public decimal  Cantidad        { get; set; }
        public string?  Unidad          { get; set; }
        public string?  Marca           { get; set; }
        public decimal  ManoObra        { get; set; }
        public decimal  PrecioUnitario  { get; set; }
        public decimal  Total           { get; set; }
        public string?  Moneda          { get; set; } = "MXN";
        public string?  TiempoEntrega   { get; set; }
        // Formato 2
        public decimal? CostoUsd        { get; set; }
        public decimal? Tc              { get; set; }
        public decimal? PctVenta        { get; set; }
        public decimal? PctMo           { get; set; }
    }
}
