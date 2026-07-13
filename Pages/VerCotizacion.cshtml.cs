using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class VerCotizacionModel : PageBase
{
    private readonly IConfiguration _config;

    public VerCotizacionModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public CotizacionDetalle? Cotizacion { get; private set; }
    public List<CotizacionItemRow> Items { get; private set; } = new();

    public bool IsNotFound { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        if (Id <= 0)
            return RedirectToPage("/Cotizaciones");

        await CargarDetalleAsync();

        if (IsNotFound)
            return RedirectToPage("/Cotizaciones");

        return Page();
    }

    private async Task CargarDetalleAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_ObtenerCotizacionDetalle", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = Id;

        await using var rdr = await cmd.ExecuteReaderAsync();

        // 1er result-set: encabezado
        if (await rdr.ReadAsync())
        {
            Cotizacion = new CotizacionDetalle
            {
                Id           = rdr.GetInt32(rdr.GetOrdinal("id")),
                Folio        = rdr.GetString(rdr.GetOrdinal("folio")),
                Fecha        = rdr.GetDateTime(rdr.GetOrdinal("fecha")),
                Cliente      = rdr.GetString(rdr.GetOrdinal("cliente")),
                Proyecto     = rdr.GetString(rdr.GetOrdinal("proyecto")),
                FechaReg     = rdr.GetDateTime(rdr.GetOrdinal("fecha_reg")),
                Rfq          = rdr.GetString(rdr.GetOrdinal("rfq")),
                DescuentoPct = rdr.GetDecimal(rdr.GetOrdinal("descuento_pct")),
                Consecutivo  = rdr.IsDBNull(rdr.GetOrdinal("consecutivo"))
                               ? null : rdr.GetInt32(rdr.GetOrdinal("consecutivo")),
            };
        }
        else
        {
            IsNotFound = true;
            return;
        }

        // 2do result-set: ítems
        if (await rdr.NextResultAsync())
        {
            var ordId      = rdr.GetOrdinal("id");
            var ordNoItem  = rdr.GetOrdinal("no_item");
            var ordDesc    = rdr.GetOrdinal("descripcion");
            var ordCant    = rdr.GetOrdinal("cantidad");
            var ordUm      = rdr.GetOrdinal("unidad");
            var ordMarca   = rdr.GetOrdinal("marca");
            var ordMo      = rdr.GetOrdinal("mano_obra");
            var ordPu      = rdr.GetOrdinal("precio_unitario");
            var ordTotal   = rdr.GetOrdinal("total");
            var ordMoneda  = rdr.GetOrdinal("moneda");
            var ordTe      = rdr.GetOrdinal("tiempo_entrega");

            while (await rdr.ReadAsync())
            {
                Items.Add(new CotizacionItemRow
                {
                    Id             = rdr.GetInt32(ordId),
                    NoItem         = rdr.GetInt32(ordNoItem),
                    Descripcion    = rdr.GetString(ordDesc),
                    Cantidad       = rdr.GetDecimal(ordCant),
                    Unidad         = rdr.GetString(ordUm),
                    Marca          = rdr.IsDBNull(ordMarca)  ? null : rdr.GetString(ordMarca),
                    ManoObra       = rdr.GetDecimal(ordMo),
                    PrecioUnitario = rdr.GetDecimal(ordPu),
                    Total          = rdr.GetDecimal(ordTotal),
                    Moneda         = rdr.GetString(ordMoneda),
                    TiempoEntrega  = rdr.IsDBNull(ordTe) ? null : rdr.GetString(ordTe),
                });
            }
        }
    }

    public class CotizacionDetalle
    {
        public int      Id           { get; set; }
        public string   Folio        { get; set; } = "";
        public DateTime Fecha        { get; set; }
        public string   Cliente      { get; set; } = "";
        public string   Proyecto     { get; set; } = "";
        public DateTime FechaReg     { get; set; }
        public string   Rfq          { get; set; } = "";
        public decimal  DescuentoPct { get; set; }
        public int?     Consecutivo  { get; set; }
    }

    public class CotizacionItemRow
    {
        public int      Id             { get; set; }
        public int      NoItem         { get; set; }
        public string   Descripcion    { get; set; } = "";
        public decimal  Cantidad       { get; set; }
        public string   Unidad         { get; set; } = "";
        public string?  Marca          { get; set; }
        public decimal  ManoObra       { get; set; }
        public decimal  PrecioUnitario { get; set; }
        public decimal  Total          { get; set; }
        public string   Moneda         { get; set; } = "";
        public string?  TiempoEntrega  { get; set; }
    }
}
