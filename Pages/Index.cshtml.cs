using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace InventarioWeb.Pages;

public class ActividadItem
{
    public string Tipo        { get; set; } = "";   // "Entrada" | "Salida"
    public string CodFab      { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public DateTime Fecha     { get; set; }
}

public class IndexModel : PageBase
{
    private readonly IConfiguration _config;
    public IndexModel(IConfiguration config) => _config = config;

    public int TotalProductos   { get; private set; }
    public int StockNegativo    { get; private set; }
    public int EntradasMes      { get; private set; }
    public int SalidasMes       { get; private set; }
    public List<ActividadItem> Actividad { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarSesion();
        if (deny != null) return deny;
        var connStr = _config.GetConnectionString("SqlServer");
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // KPIs en una sola consulta multi-result
            var sqlKpi = @"
SELECT COUNT(*) FROM inventario.catalogo_materiales WHERE cod_fab != 'ND';
SELECT COUNT(*) FROM inventario.catalogo_materiales WHERE cant < 0 AND cod_fab != 'ND';
SELECT COUNT(*) FROM inventario.entradas_inventario
  WHERE MONTH(fecha_compra) = MONTH(GETDATE()) AND YEAR(fecha_compra) = YEAR(GETDATE());
SELECT COUNT(*) FROM inventario.salidas_inventario
  WHERE MONTH(fecha_salida) = MONTH(GETDATE()) AND YEAR(fecha_salida) = YEAR(GETDATE());";

            await using (var cmd = new SqlCommand(sqlKpi, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                if (await rdr.ReadAsync()) TotalProductos = rdr.GetInt32(0);
                await rdr.NextResultAsync();
                if (await rdr.ReadAsync()) StockNegativo  = rdr.GetInt32(0);
                await rdr.NextResultAsync();
                if (await rdr.ReadAsync()) EntradasMes    = rdr.GetInt32(0);
                await rdr.NextResultAsync();
                if (await rdr.ReadAsync()) SalidasMes     = rdr.GetInt32(0);
            }

            // Actividad reciente (últimos 6 movimientos)
            var sqlAct = @"
SELECT TOP 6 tipo, cod_fab, descripcion, fecha FROM (
    SELECT 'Entrada' tipo, cod_fab, descripcion, fecha_compra fecha
    FROM inventario.entradas_inventario
    UNION ALL
    SELECT 'Salida' tipo, cod_fab, descripcion, fecha_salida fecha
    FROM inventario.salidas_inventario
) t ORDER BY fecha DESC;";

            await using (var cmd2 = new SqlCommand(sqlAct, conn))
            await using (var rdr2 = await cmd2.ExecuteReaderAsync())
            {
                while (await rdr2.ReadAsync())
                {
                    Actividad.Add(new ActividadItem
                    {
                        Tipo        = rdr2.GetString(0),
                        CodFab      = rdr2.IsDBNull(1) ? "" : rdr2.GetString(1),
                        Descripcion = rdr2.IsDBNull(2) ? "" : rdr2.GetString(2),
                        Fecha       = rdr2.GetDateTime(3),
                    });
                }
            }
        }
        catch { /* DB no disponible — se muestran ceros */ }

        return Page();
    }
}
