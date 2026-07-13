using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace InventarioWeb.Pages;

public class ImprimirVolumetriaModel : PageBase
{
    private readonly IConfiguration _config;
    public ImprimirVolumetriaModel(IConfiguration config) => _config = config;

    [BindProperty(SupportsGet = true)] public string? Entradas   { get; set; }
    [BindProperty(SupportsGet = true)] public string? Salidas    { get; set; }
    [BindProperty(SupportsGet = true)] public string? Materiales { get; set; }

    public List<EntradaRow>  ListaEntradas   { get; private set; } = new();
    public List<SalidaRow>   ListaSalidas    { get; private set; } = new();
    public List<MaterialRow> ListaMateriales { get; private set; } = new();

    // Sumas por código, solo de lo seleccionado
    public Dictionary<string, decimal> EntradaPorCod { get; private set; } = new();
    public Dictionary<string, decimal> SalidaPorCod  { get; private set; } = new();

    // Hoja Volumetría: salidas agrupadas por (código, proyecto), con
    // Instalado = Salida − Devolución calculado automáticamente
    public List<VolumetriaGrupoRow> VolumetriaGrupos { get; private set; } = new();

    // Encabezado (autocompletado si se detecta)
    public string HdrCliente   { get; private set; } = "";
    public string HdrProyecto  { get; private set; } = "";
    public string HdrLocalidad { get; private set; } = "";
    public string HdrOt        { get; private set; } = "";
    public string HdrPo        { get; private set; } = "";

    // Lista completa de proyectos para el selector del encabezado
    public List<ProyectoOpcion> Proyectos { get; private set; } = new();

    // Lista completa de clientes (razón social) para el selector del encabezado
    public List<string> Clientes { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        var idsEntradas = ParseInts(Entradas);
        var idsSalidas  = ParseInts(Salidas);
        var codsMat     = ParseStrings(Materiales);

        if (idsEntradas.Count == 0 && idsSalidas.Count == 0 && codsMat.Count == 0)
            return RedirectToPage("/Volumetrias");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        if (idsEntradas.Count > 0) await CargarEntradasAsync(conn, idsEntradas);
        if (idsSalidas.Count  > 0) await CargarSalidasAsync(conn, idsSalidas);
        if (codsMat.Count     > 0) await CargarMaterialesAsync(conn, codsMat);

        if (ListaSalidas.Count > 0) await CargarVolumetriaGruposAsync(conn);

        // Listas para los selectores del encabezado
        await CargarProyectosAsync(conn);
        await CargarClientesAsync(conn);

        // Sumas por código (solo lo seleccionado)
        EntradaPorCod = ListaEntradas
            .GroupBy(e => e.CodFab.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad), StringComparer.OrdinalIgnoreCase);
        SalidaPorCod = ListaSalidas
            .GroupBy(s => s.CodFab.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad), StringComparer.OrdinalIgnoreCase);

        // PO: primer no_po de las entradas seleccionadas
        HdrPo = ListaEntradas.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.NoPo))?.NoPo?.Trim() ?? "";

        // Proyecto detectado desde salidas o entradas seleccionadas
        var proyDetectado =
            ListaSalidas.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.ProyectoAsignado))?.ProyectoAsignado
            ?? ListaEntradas.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Proyecto))?.Proyecto;

        if (!string.IsNullOrWhiteSpace(proyDetectado))
            await CargarEncabezadoAsync(conn, proyDetectado.Trim());

        return Page();
    }

    // Trae PROYECTO desde administracion_proyectos.proyectos y CLIENTE desde inventario.cat_clientes
    private async Task CargarEncabezadoAsync(SqlConnection conn, string proyecto)
    {
        // El proyecto puede venir como "8444 - Descripción"; usar la parte antes de " - " como cotización
        var cotizacion = proyecto;
        var sep = proyecto.IndexOf(" - ", StringComparison.Ordinal);
        if (sep > 0) cotizacion = proyecto.Substring(0, sep).Trim();

        try
        {
            // proyectos.cliente guarda el id_cliente → unir con cat_clientes para la razón social.
            // Si no hay match por id, usar el valor tal cual (por si guarda el nombre).
            var sql = @"
                SELECT TOP 1
                    ISNULL(p.proyecto, '') AS proyecto,
                    ISNULL(cc.razon_social, ISNULL(CAST(p.cliente AS NVARCHAR(200)), '')) AS cliente,
                    ISNULL(CAST(p.numero_po AS NVARCHAR(100)), '') AS numero_po
                FROM administracion_proyectos.proyectos p
                LEFT JOIN inventario.cat_clientes cc
                       ON cc.id_cliente = TRY_CAST(p.cliente AS INT)
                WHERE CAST(p.cotizacion AS NVARCHAR(50)) = @p
                ORDER BY p.cotizacion DESC;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@p", SqlDbType.NVarChar, 50).Value = cotizacion;
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                HdrProyecto = rdr.GetString(0);
                HdrCliente  = rdr.GetString(1);
                var poProy  = rdr.GetString(2).Trim();
                if (!string.IsNullOrEmpty(poProy)) HdrPo = poProy;   // PO desde proyectos.numero_po
            }
        }
        catch { /* si falla la detección, el encabezado queda editable en blanco */ }

        // OT = número de cotización detectado
        if (string.IsNullOrEmpty(HdrOt)) HdrOt = cotizacion;
    }

    // Trae TODOS los proyectos para el selector del encabezado (igual que el módulo de Salida de Material)
    private async Task CargarProyectosAsync(SqlConnection conn)
    {
        try
        {
            var sql = @"
                SELECT CAST(p.cotizacion AS NVARCHAR(50)) AS cotizacion,
                       ISNULL(LTRIM(RTRIM(p.proyecto)),'') AS proyecto,
                       ISNULL(cc.razon_social, ISNULL(CAST(p.cliente AS NVARCHAR(200)),'')) AS cliente,
                       ISNULL(CAST(p.numero_po AS NVARCHAR(100)),'') AS numero_po
                FROM administracion_proyectos.proyectos p
                LEFT JOIN inventario.cat_clientes cc
                       ON cc.id_cliente = TRY_CAST(p.cliente AS INT)
                WHERE p.cotizacion IS NOT NULL
                ORDER BY p.cotizacion DESC;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var cot = rdr.GetString(0).Trim();
                if (string.IsNullOrEmpty(cot)) continue;
                Proyectos.Add(new ProyectoOpcion
                {
                    Cotizacion = cot,
                    Proyecto   = rdr.GetString(1).Trim(),
                    Cliente    = rdr.GetString(2).Trim(),
                    NumeroPo   = rdr.GetString(3).Trim(),
                });
            }
        }
        catch { /* si falla, el selector queda vacío y el encabezado sigue editable */ }
    }

    // Trae TODOS los clientes (razón social) para el selector del encabezado
    private async Task CargarClientesAsync(SqlConnection conn)
    {
        try
        {
            var sql = @"
                SELECT DISTINCT LTRIM(RTRIM(razon_social)) AS razon_social
                FROM inventario.cat_clientes
                WHERE razon_social IS NOT NULL
                  AND LTRIM(RTRIM(razon_social)) <> ''
                  AND (bajalogica IS NULL OR bajalogica = 0)
                ORDER BY razon_social;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var rs = rdr.GetString(0).Trim();
                if (!string.IsNullOrEmpty(rs)) Clientes.Add(rs);
            }
        }
        catch { /* si falla, el selector queda vacío y el encabezado sigue editable */ }
    }

    private async Task CargarEntradasAsync(SqlConnection conn, List<int> ids)
    {
        var pn = ids.Select((_, i) => $"@e{i}").ToList();
        var sql = $@"
            SELECT e.id, ISNULL(e.cod_fab,'') AS cod_fab, ISNULL(e.descripcion,'') AS descripcion,
                   e.cantidad, ISNULL(e.um,'') AS um, ISNULL(e.proveedor,'') AS proveedor,
                   e.fecha_compra, e.no_po, e.fac, e.proyecto,
                   ISNULL(c.marca,'') AS marca
            FROM inventario.entradas_inventario e
            LEFT JOIN inventario.catalogo_materiales c ON LTRIM(RTRIM(e.cod_fab)) = LTRIM(RTRIM(c.cod_fab))
            WHERE e.id IN ({string.Join(",", pn)})
            ORDER BY e.fecha_compra, e.id;";
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(pn[i], ids[i]);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            ListaEntradas.Add(new EntradaRow
            {
                Id          = rdr.GetInt32(0),
                CodFab      = rdr.GetString(1),
                Descripcion = rdr.GetString(2),
                Cantidad    = rdr.GetDecimal(3),
                Um          = rdr.GetString(4),
                Proveedor   = rdr.GetString(5),
                FechaCompra = rdr.GetDateTime(6),
                NoPo        = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                Fac         = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                Proyecto    = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                Marca       = rdr.GetString(10),
            });
        }
    }

    // Extrae el número de cotización inicial de un texto de proyecto ("8556" o
    // "8556 - Nombre del proyecto" → "8556"), para poder comparar/agrupar sin
    // importar el formato en que haya quedado guardado.
    private static string NumProyecto(string? s)
    {
        var t = (s ?? "").Trim();
        var m = Regex.Match(t, @"^(\d+)");
        return m.Success ? m.Groups[1].Value : t;
    }

    // Hoja Volumetría: agrupa las salidas seleccionadas por (código, proyecto)
    // y calcula Instalado = Salida − Devolución, sumando lo que aparezca en
    // inventario.devoluciones_inventario para ese mismo código y proyecto.
    private async Task CargarVolumetriaGruposAsync(SqlConnection conn)
    {
        var codigos = ListaSalidas.Select(s => s.CodFab.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var devueltoPorGrupo = new Dictionary<(string cod, string proy), decimal>();
        if (codigos.Count > 0)
        {
            var pn = codigos.Select((_, i) => $"@c{i}").ToList();
            var sql = $@"
                SELECT ISNULL(cod_fab,'') AS cod_fab, ISNULL(proyecto,'') AS proyecto, cantidad
                FROM inventario.devoluciones_inventario
                WHERE cod_fab IN ({string.Join(",", pn)});";
            await using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < codigos.Count; i++)
                cmd.Parameters.Add(pn[i], SqlDbType.NVarChar, 200).Value = codigos[i];
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var cod  = rdr.GetString(0).Trim().ToUpperInvariant();
                var proy = NumProyecto(rdr.GetString(1));
                var cant = rdr.GetDecimal(2);
                var key  = (cod, proy);
                devueltoPorGrupo[key] = devueltoPorGrupo.TryGetValue(key, out var acc) ? acc + cant : cant;
            }
        }

        VolumetriaGrupos = ListaSalidas
            .GroupBy(s => (Cod: s.CodFab.Trim().ToUpperInvariant(), Proy: NumProyecto(s.ProyectoAsignado)))
            .Select(g =>
            {
                var cantidad = g.Sum(x => x.Cantidad);
                var devuelto = devueltoPorGrupo.TryGetValue(g.Key, out var d) ? d : 0m;
                var instalado = Math.Max(0, cantidad - devuelto);

                var fechas = g.Select(x => x.FechaSalida.Date).Distinct().OrderBy(f => f).ToList();
                var folios = g.Select(x => x.NoSalida).Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct().ToList();
                var recibes = g.Select(x => x.Recibe).Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                return new VolumetriaGrupoRow
                {
                    CodFab      = g.First().CodFab.Trim(),
                    Descripcion = g.First().Descripcion,
                    Cantidad    = cantidad,
                    Um          = g.First().Um,
                    Fecha       = string.Join(" / ", fechas.Select(f => f.ToString("dd/MM/yyyy"))),
                    NoSalida    = string.Join(", ", folios),
                    Recibe      = recibes.Count switch { 0 => "", 1 => recibes[0], _ => "Varios" },
                    Instalado   = instalado,
                };
            })
            .OrderBy(g => g.CodFab)
            .ToList();
    }

    private async Task CargarSalidasAsync(SqlConnection conn, List<int> ids)
    {
        var pn = ids.Select((_, i) => $"@s{i}").ToList();
        var sql = $@"
            SELECT s.id, ISNULL(s.cod_fab,'') AS cod_fab, ISNULL(s.descripcion,'') AS descripcion,
                   s.cantidad, ISNULL(s.um,'') AS um, s.fecha_salida, s.no_salida,
                   ISNULL(s.recibe,'') AS recibe, ISNULL(s.instalado,'') AS instalado,
                   ISNULL(s.proyecto_asignado,'') AS proyecto_asignado, s.balance
            FROM inventario.salidas_inventario s
            WHERE s.id IN ({string.Join(",", pn)})
            ORDER BY s.fecha_salida, s.id;";
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(pn[i], ids[i]);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            ListaSalidas.Add(new SalidaRow
            {
                Id               = rdr.GetInt32(0),
                CodFab           = rdr.GetString(1),
                Descripcion      = rdr.GetString(2),
                Cantidad         = rdr.GetDecimal(3),
                Um               = rdr.GetString(4),
                FechaSalida      = rdr.GetDateTime(5),
                NoSalida         = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                Recibe           = rdr.GetString(7),
                Instalado        = rdr.GetString(8),
                ProyectoAsignado = rdr.GetString(9),
                Balance          = rdr.GetDecimal(10),
            });
        }
    }

    private async Task CargarMaterialesAsync(SqlConnection conn, List<string> cods)
    {
        var pn = cods.Select((_, i) => $"@m{i}").ToList();
        var sql = $@"
            SELECT ISNULL(c.cod_fab,'') AS cod_fab,
                   ISNULL(MAX(ei.cod_int),'') AS cod_int,
                   ISNULL(c.no_part,'') AS no_part,
                   ISNULL(c.descripcion,'') AS descripcion, ISNULL(c.um,'') AS um,
                   ISNULL(c.cant,0) AS cant, ISNULL(c.pu,0) AS pu, ISNULL(c.moneda,'MXN') AS moneda,
                   ISNULL(c.partida,'') AS partida, ISNULL(c.proveedor,'') AS proveedor,
                   ISNULL(c.marca,'') AS marca
            FROM inventario.catalogo_materiales c
            LEFT JOIN inventario.entradas_inventario ei
                   ON LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(ei.cod_fab))
            WHERE LTRIM(RTRIM(c.cod_fab)) IN ({string.Join(",", pn)})
            GROUP BY c.cod_fab, c.no_part, c.descripcion, c.um, c.cant, c.pu, c.moneda, c.partida, c.proveedor, c.marca
            ORDER BY c.cod_fab;";
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < cods.Count; i++)
            cmd.Parameters.Add(pn[i], SqlDbType.NVarChar, 200).Value = cods[i];
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            ListaMateriales.Add(new MaterialRow
            {
                CodFab      = rdr.GetString(0),
                CodInt      = rdr.GetString(1),
                NoPart      = rdr.GetString(2),
                Descripcion = rdr.GetString(3),
                Um          = rdr.GetString(4),
                Cant        = rdr.GetDecimal(5),
                Pu          = rdr.GetDecimal(6),
                Moneda      = rdr.GetString(7),
                Partida     = rdr.GetString(8),
                Proveedor   = rdr.GetString(9),
                Marca       = rdr.GetString(10),
            });
        }
    }

    private static List<int> ParseInts(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => s.Trim())
                 .Where(s => int.TryParse(s, out _))
                 .Select(int.Parse)
                 .Distinct()
                 .ToList();

    private static List<string> ParseStrings(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => s.Trim())
                 .Where(s => s.Length > 0)
                 .Distinct()
                 .ToList();

    public class EntradaRow
    {
        public int      Id          { get; set; }
        public string   CodFab      { get; set; } = "";
        public string   Descripcion { get; set; } = "";
        public decimal  Cantidad    { get; set; }
        public string   Um          { get; set; } = "";
        public string   Proveedor   { get; set; } = "";
        public DateTime FechaCompra { get; set; }
        public string?  NoPo        { get; set; }
        public string?  Fac         { get; set; }
        public string?  Proyecto    { get; set; }
        public string   Marca       { get; set; } = "";
    }

    public class SalidaRow
    {
        public int      Id               { get; set; }
        public string   CodFab           { get; set; } = "";
        public string   Descripcion      { get; set; } = "";
        public decimal  Cantidad         { get; set; }
        public string   Um               { get; set; } = "";
        public DateTime FechaSalida      { get; set; }
        public string?  NoSalida         { get; set; }
        public string   Recibe           { get; set; } = "";
        public string   Instalado        { get; set; } = "";
        public string   ProyectoAsignado { get; set; } = "";
        public decimal  Balance          { get; set; }
    }

    public class VolumetriaGrupoRow
    {
        public string  CodFab      { get; set; } = "";
        public string  Descripcion { get; set; } = "";
        public decimal Cantidad    { get; set; }
        public string  Um          { get; set; } = "";
        public string  Fecha       { get; set; } = "";
        public string  NoSalida    { get; set; } = "";
        public string  Recibe      { get; set; } = "";
        public decimal Instalado   { get; set; }
        public decimal Balance     => Cantidad - Instalado;
    }

    public class ProyectoOpcion
    {
        public string Cotizacion { get; set; } = "";
        public string Proyecto   { get; set; } = "";
        public string Cliente    { get; set; } = "";
        public string NumeroPo   { get; set; } = "";
    }

    public class MaterialRow
    {
        public string  CodFab      { get; set; } = "";
        public string  CodInt      { get; set; } = "";
        public string  NoPart      { get; set; } = "";
        public string  Descripcion { get; set; } = "";
        public string  Um          { get; set; } = "";
        public decimal Cant        { get; set; }
        public decimal Pu          { get; set; }
        public string  Moneda      { get; set; } = "MXN";
        public string  Partida     { get; set; } = "";
        public string  Proveedor   { get; set; } = "";
        public string  Marca       { get; set; } = "";
    }
}
