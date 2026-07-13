using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class ImprimirEntradasModel : PageBase
{
    private readonly IConfiguration _config;
    public ImprimirEntradasModel(IConfiguration config) => _config = config;

    [BindProperty(SupportsGet = true)]
    public string? Ids { get; set; }

    public List<FilaEntrada> Entradas { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        if (string.IsNullOrWhiteSpace(Ids))
            return RedirectToPage("/EntradaMaterial");

        var idList = Ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim())
                         .Where(s => int.TryParse(s, out _))
                         .Select(int.Parse)
                         .Distinct()
                         .ToList();

        if (idList.Count == 0)
            return RedirectToPage("/EntradaMaterial");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var paramNames = idList.Select((_, i) => $"@id{i}").ToList();
        var sql = $@"
            SELECT
                e.id,
                ISNULL(e.cod_fab,     'ND') AS cod_fab,
                ISNULL(e.cod_int,     '')   AS cod_int,
                ISNULL(e.descripcion, '')   AS descripcion,
                e.cantidad,
                ISNULL(e.um,          '')   AS um,
                e.fecha_compra,
                ISNULL(e.proveedor,   '')   AS proveedor,
                e.no_po,
                e.fac,
                e.proyecto,
                ISNULL(c.marca,       '')   AS marca
            FROM inventario.entradas_inventario e
            LEFT JOIN inventario.catalogo_materiales c
                   ON LTRIM(RTRIM(e.cod_fab)) = LTRIM(RTRIM(c.cod_fab))
            WHERE e.id IN ({string.Join(",", paramNames)})
            ORDER BY e.fecha_compra, e.id;";

        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < idList.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], idList[i]);

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            Entradas.Add(new FilaEntrada
            {
                Id          = rdr.GetInt32(0),
                CodFab      = rdr.GetString(1),
                CodInt      = rdr.GetString(2),
                Descripcion = rdr.GetString(3),
                Cantidad    = rdr.GetDecimal(4),
                Um          = rdr.GetString(5),
                FechaCompra = rdr.GetDateTime(6),
                Proveedor   = rdr.GetString(7),
                NoPo        = rdr.IsDBNull(8)  ? null : rdr.GetString(8),
                Fac         = rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                Proyecto    = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                Marca       = rdr.GetString(11),
            });
        }

        return Page();
    }

    public class FilaEntrada
    {
        public int      Id          { get; set; }
        public string   CodFab      { get; set; } = "";
        public string   CodInt      { get; set; } = "";
        public string   Descripcion { get; set; } = "";
        public decimal  Cantidad    { get; set; }
        public string   Um          { get; set; } = "";
        public DateTime FechaCompra { get; set; }
        public string   Proveedor   { get; set; } = "";
        public string?  NoPo        { get; set; }
        public string?  Fac         { get; set; }
        public string?  Proyecto    { get; set; }
        public string   Marca       { get; set; } = "";
    }
}
