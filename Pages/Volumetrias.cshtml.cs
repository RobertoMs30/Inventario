using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class VolumetriasModel : PageBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<VolumetriasModel> _logger;

    public VolumetriasModel(IConfiguration config, ILogger<VolumetriasModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Tablas
    public List<EntradaRow>  Entradas  { get; private set; } = new();
    public List<SalidaRow>   Salidas   { get; private set; } = new();
    public List<CatalogoRow> Catalogo  { get; private set; } = new();

    // Búsqueda del catálogo (columna Estimación)
    [BindProperty(SupportsGet = true)] public string? QCat { get; set; }

    public int TotalEntradas => Entradas.Count;
    public int TotalSalidas  => Salidas.Count;
    public int TotalCatalogo { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        await Task.WhenAll(
            CargarEntradasAsync(),
            CargarSalidasAsync(),
            CargarCatalogoAsync());

        return Page();
    }

    // ── Autocomplete: buscar proyecto por número de cotización o nombre ──────
    // Se busca en el catálogo completo de proyectos (no solo los que aparecen
    // en las últimas 200 entradas/salidas), para poder filtrar por cualquier
    // proyecto existente.
    public async Task<IActionResult> OnGetBuscarProyectosAsync(string q)
    {
        var deny = VerificarAccesoJson("inventario");
        if (deny != null) return deny;
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<object>());

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP 15
                CAST(cotizacion AS NVARCHAR(50)) AS cotizacion,
                ISNULL(proyecto, '') AS proyecto
            FROM administracion_proyectos.proyectos
            WHERE CAST(cotizacion AS NVARCHAR(50)) LIKE '%'+@q+'%'
               OR ISNULL(proyecto,'') LIKE '%'+@q+'%'
            ORDER BY cotizacion DESC;";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = q.Trim();

        var results = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            results.Add(new {
                cotizacion = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                proyecto   = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
            });

        return new JsonResult(results);
    }

    private async Task CargarEntradasAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 200
    e.id, e.cod_fab, e.descripcion, e.cantidad, e.um, e.proveedor, e.fecha_compra, e.proyecto
FROM inventario.entradas_inventario e
ORDER BY e.id DESC;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            Entradas.Clear();
            while (await rdr.ReadAsync())
            {
                Entradas.Add(new EntradaRow
                {
                    Id          = rdr.GetInt32(0),
                    CodFab      = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    Descripcion = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    Cantidad    = rdr.GetDecimal(3),
                    Um          = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                    Proveedor   = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                    FechaCompra = rdr.GetDateTime(6),
                    Proyecto    = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar entradas en Volumetrías"); }
    }

    private async Task CargarSalidasAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = @"
SELECT TOP 200
    s.id, s.cod_fab, s.descripcion, s.cantidad, s.um, s.fecha_salida, s.proyecto_asignado
FROM inventario.salidas_inventario s
ORDER BY s.fecha_salida DESC, s.id DESC;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            Salidas.Clear();
            while (await rdr.ReadAsync())
            {
                Salidas.Add(new SalidaRow
                {
                    Id               = rdr.GetInt32(0),
                    CodFab           = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    Descripcion      = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    Cantidad         = rdr.GetDecimal(3),
                    Um               = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                    FechaSalida      = rdr.GetDateTime(5),
                    ProyectoAsignado = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar salidas en Volumetrías"); }
    }

    private async Task CargarCatalogoAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("SqlServer");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var q = string.IsNullOrWhiteSpace(QCat) ? null : QCat.Trim();

            var sql = @"
SELECT TOP 500
    c.cod_fab,
    ISNULL(c.descripcion, '') AS descripcion,
    ISNULL(c.um, '')          AS um,
    ISNULL(c.cant, 0)         AS cant,
    ISNULL(c.pu, 0)           AS pu,
    ISNULL(c.moneda, 'MXN')   AS moneda
FROM inventario.catalogo_materiales c
WHERE (@q IS NULL OR @q = ''
       OR c.cod_fab     LIKE '%' + @q + '%'
       OR c.descripcion LIKE '%' + @q + '%')
ORDER BY c.cod_fab;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 200).Value = (object?)q ?? DBNull.Value;
            await using var rdr = await cmd.ExecuteReaderAsync();
            Catalogo.Clear();
            while (await rdr.ReadAsync())
            {
                Catalogo.Add(new CatalogoRow
                {
                    CodFab      = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                    Descripcion = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    Um          = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    Cant        = rdr.GetDecimal(3),
                    Pu          = rdr.GetDecimal(4),
                    Moneda      = rdr.IsDBNull(5) ? "MXN" : rdr.GetString(5),
                });
            }
            TotalCatalogo = Catalogo.Count;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo cargar catálogo en Volumetrías"); }
    }

    public class EntradaRow
    {
        public int      Id          { get; set; }
        public string   CodFab      { get; set; } = "";
        public string   Descripcion { get; set; } = "";
        public decimal  Cantidad    { get; set; }
        public string   Um          { get; set; } = "";
        public string   Proveedor   { get; set; } = "";
        public DateTime FechaCompra { get; set; }
        public string?  Proyecto    { get; set; }
    }

    public class SalidaRow
    {
        public int      Id               { get; set; }
        public string   CodFab           { get; set; } = "";
        public string   Descripcion      { get; set; } = "";
        public decimal  Cantidad         { get; set; }
        public string   Um               { get; set; } = "";
        public DateTime FechaSalida      { get; set; }
        public string?  ProyectoAsignado { get; set; }
    }

    public class CatalogoRow
    {
        public string  CodFab      { get; set; } = "";
        public string  Descripcion { get; set; } = "";
        public string  Um          { get; set; } = "";
        public decimal Cant        { get; set; }
        public decimal Pu          { get; set; }
        public string  Moneda      { get; set; } = "MXN";
    }
}
