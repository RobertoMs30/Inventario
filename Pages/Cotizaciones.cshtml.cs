using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class CotizacionesModel : PageBase
{
    private readonly IConfiguration _config;

    public CotizacionesModel(IConfiguration config)
    {
        _config = config;
    }

    public List<CotizacionRow> Cotizaciones { get; private set; } = new();

    // Búsqueda
    public string? Q { get; private set; }

    // Paginación
    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 25;
    public int TotalRecords { get; private set; }
    public int TotalPages => TotalRecords > 0 ? (TotalRecords + PageSize - 1) / PageSize : 1;

    public async Task<IActionResult> OnPostEliminarAsync(int id, string? q, int p = 1)
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_EliminarCotizacion", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@cotizacion_id", SqlDbType.Int).Value = id;
        await cmd.ExecuteNonQueryAsync();

        var qs = string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}";
        return Redirect($"/Cotizaciones?p={p}&deleted=1{qs}");
    }

    public async Task<IActionResult> OnGetAsync(string? q, int p = 1)
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        Q = q?.Trim();
        if (p < 1) p = 1;
        CurrentPage = p;

        await CargarCotizacionesAsync();

        return Page();
    }

    private async Task CargarCotizacionesAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        int offset = (CurrentPage - 1) * PageSize;

        await using var cmd = new SqlCommand("dbo.sp_ObtenerCotizaciones", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(Q) ? (object)DBNull.Value : Q;
        cmd.Parameters.Add("@offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@pageSize", SqlDbType.Int).Value = PageSize;

        await using var rdr = await cmd.ExecuteReaderAsync();

        // Primer result-set: total
        if (await rdr.ReadAsync())
            TotalRecords = rdr.GetInt32(0);

        // Ajustar página si excede total
        if (CurrentPage > TotalPages && TotalPages > 0)
        {
            CurrentPage = TotalPages;
            offset = (CurrentPage - 1) * PageSize;
            // Releer con offset corregido (simple: volvemos a ejecutar)
            await rdr.CloseAsync();
            await cmd.DisposeAsync();
            await CargarCotizacionesConOffsetAsync(conn, offset);
            return;
        }

        // Segundo result-set: registros
        if (await rdr.NextResultAsync())
        {
            var ordId        = rdr.GetOrdinal("id");
            var ordFolio     = rdr.GetOrdinal("folio");
            var ordFecha     = rdr.GetOrdinal("fecha");
            var ordCliente   = rdr.GetOrdinal("cliente");
            var ordProyecto  = rdr.GetOrdinal("proyecto");
            var ordFechaReg  = rdr.GetOrdinal("fecha_reg");

            while (await rdr.ReadAsync())
            {
                Cotizaciones.Add(new CotizacionRow
                {
                    Id       = rdr.GetInt32(ordId),
                    Folio    = rdr.GetString(ordFolio),
                    Fecha    = rdr.GetDateTime(ordFecha),
                    Cliente  = rdr.GetString(ordCliente),
                    Proyecto = rdr.GetString(ordProyecto),
                    FechaReg = rdr.GetDateTime(ordFechaReg),
                });
            }
        }
    }

    private async Task CargarCotizacionesConOffsetAsync(SqlConnection conn, int offset)
    {
        await using var cmd2 = new SqlCommand("dbo.sp_ObtenerCotizaciones", conn);
        cmd2.CommandType = CommandType.StoredProcedure;
        cmd2.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(Q) ? (object)DBNull.Value : Q;
        cmd2.Parameters.Add("@offset", SqlDbType.Int).Value = offset;
        cmd2.Parameters.Add("@pageSize", SqlDbType.Int).Value = PageSize;

        await using var rdr2 = await cmd2.ExecuteReaderAsync();

        // Saltar total
        if (await rdr2.ReadAsync()) { /* ya tenemos TotalRecords */ }

        if (await rdr2.NextResultAsync())
        {
            var ordId        = rdr2.GetOrdinal("id");
            var ordFolio     = rdr2.GetOrdinal("folio");
            var ordFecha     = rdr2.GetOrdinal("fecha");
            var ordCliente   = rdr2.GetOrdinal("cliente");
            var ordProyecto  = rdr2.GetOrdinal("proyecto");
            var ordFechaReg  = rdr2.GetOrdinal("fecha_reg");

            while (await rdr2.ReadAsync())
            {
                Cotizaciones.Add(new CotizacionRow
                {
                    Id       = rdr2.GetInt32(ordId),
                    Folio    = rdr2.GetString(ordFolio),
                    Fecha    = rdr2.GetDateTime(ordFecha),
                    Cliente  = rdr2.GetString(ordCliente),
                    Proyecto = rdr2.GetString(ordProyecto),
                    FechaReg = rdr2.GetDateTime(ordFechaReg),
                });
            }
        }
    }
    
    public class CotizacionRow
    {
        public int      Id       { get; set; }
        public string   Folio    { get; set; } = "";
        public DateTime Fecha    { get; set; }
        public string   Cliente  { get; set; } = "";
        public string   Proyecto { get; set; } = "";
        public DateTime FechaReg { get; set; }
    }
}
