using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class ImprimirSalidasModel : PageBase
{
    private readonly IConfiguration _config;
    public ImprimirSalidasModel(IConfiguration config) => _config = config;

    [BindProperty(SupportsGet = true)]
    public string? Proyecto { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Ids { get; set; }

    // ── Encabezado ──────────────────────────────────────────
    public string Ot { get; private set; } = "";

    // ── Tabla de salidas ─────────────────────────────────────
    public List<FilaSalida> Salidas { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        bool usandoIds = !string.IsNullOrWhiteSpace(Ids);
        if (!usandoIds && string.IsNullOrWhiteSpace(Proyecto))
            return RedirectToPage("/SalidaMaterial");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        if (usandoIds)
        {
            var idList = Ids!.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => int.TryParse(s, out _))
                             .Select(int.Parse)
                             .Distinct()
                             .ToList();

            if (idList.Count == 0)
                return RedirectToPage("/SalidaMaterial");

            var paramNames = idList.Select((_, i) => $"@id{i}").ToList();
            var sqlSal = $@"
                SELECT
                    s.id,
                    ISNULL(s.cod_fab,            'ND') AS cod_fab,
                    ISNULL(e.cod_int,            '')   AS cod_int,
                    ISNULL(s.descripcion,        '')   AS descripcion,
                    s.cantidad,
                    ISNULL(s.um,                 '')   AS um,
                    s.fecha_salida,
                    s.no_salida,
                    ISNULL(s.recibe,             '')   AS recibe,
                    ISNULL(s.instalado,          '')   AS instalado,
                    s.balance,
                    ISNULL(s.proyecto_asignado,  '')   AS proyecto_asignado,
                    ISNULL(s.obs,                '')   AS obs
                FROM inventario.salidas_inventario s
                LEFT JOIN (
                    SELECT DISTINCT cod_fab, MAX(cod_int) AS cod_int
                    FROM inventario.entradas_inventario
                    GROUP BY cod_fab
                ) e ON LTRIM(RTRIM(s.cod_fab)) = LTRIM(RTRIM(e.cod_fab))
                WHERE s.id IN ({string.Join(",", paramNames)})
                ORDER BY s.fecha_salida, s.id;";

            await using var cmdSal = new SqlCommand(sqlSal, conn);
            for (int i = 0; i < idList.Count; i++)
                cmdSal.Parameters.AddWithValue(paramNames[i], idList[i]);

            string? proyectoDetectado = null;

            // Bloque propio para que el reader se cierre antes de abrir otro en CargarEncabezadoAsync
            await using (var rdrSal = await cmdSal.ExecuteReaderAsync())
            {
                while (await rdrSal.ReadAsync())
                {
                    Salidas.Add(new FilaSalida
                    {
                        Id               = rdrSal.GetInt32(0),
                        CodFab           = rdrSal.GetString(1),
                        CodInt           = rdrSal.GetString(2),
                        Descripcion      = rdrSal.GetString(3),
                        Cantidad         = rdrSal.GetDecimal(4),
                        Um               = rdrSal.GetString(5),
                        FechaSalida      = rdrSal.GetDateTime(6),
                        NoSalida         = rdrSal.IsDBNull(7)  ? null : rdrSal.GetString(7),
                        Recibe           = rdrSal.GetString(8),
                        Instalado        = rdrSal.GetString(9),
                        Balance          = rdrSal.GetDecimal(10),
                        ProyectoAsignado = rdrSal.GetString(11),
                        Obs              = rdrSal.GetString(12),
                    });
                    if (proyectoDetectado == null && !string.IsNullOrEmpty(rdrSal.GetString(11)))
                        proyectoDetectado = rdrSal.GetString(11);
                }
            } // rdrSal cerrado aquí — ahora es seguro abrir otro reader en la misma conexión

            // Cargar encabezado desde cotización del proyecto detectado
            if (!string.IsNullOrWhiteSpace(proyectoDetectado))
            {
                Proyecto = proyectoDetectado;
                await CargarEncabezadoAsync(conn, proyectoDetectado);
            }
            if (string.IsNullOrEmpty(Ot))
                Ot = Proyecto ?? "Selección";

            return Page();
        }

        // ── Modo por proyecto ────────────────────────────────────────────────
        await CargarEncabezadoAsync(conn, Proyecto!.Trim());
        if (string.IsNullOrEmpty(Ot))
            Ot = Proyecto.Trim();

        var sqlSalProy = @"
            SELECT
                s.id,
                ISNULL(s.cod_fab,            'ND') AS cod_fab,
                ISNULL(e.cod_int,            '')   AS cod_int,
                ISNULL(s.descripcion,        '')   AS descripcion,
                s.cantidad,
                ISNULL(s.um,                 '')   AS um,
                s.fecha_salida,
                s.no_salida,
                ISNULL(s.recibe,             '')   AS recibe,
                ISNULL(s.instalado,          '')   AS instalado,
                s.balance,
                ISNULL(s.proyecto_asignado,  '')   AS proyecto_asignado,
                ISNULL(s.obs,                '')   AS obs
            FROM inventario.salidas_inventario s
            LEFT JOIN (
                SELECT DISTINCT cod_fab, MAX(cod_int) AS cod_int
                FROM inventario.entradas_inventario
                GROUP BY cod_fab
            ) e ON LTRIM(RTRIM(s.cod_fab)) = LTRIM(RTRIM(e.cod_fab))
            WHERE s.proyecto_asignado = @p
            ORDER BY s.fecha_salida, s.id;";

        await using var cmdSalProy = new SqlCommand(sqlSalProy, conn);
        cmdSalProy.Parameters.Add("@p", SqlDbType.NVarChar, 200).Value = Proyecto.Trim();
        await using var rdrSalProy = await cmdSalProy.ExecuteReaderAsync();
        while (await rdrSalProy.ReadAsync())
        {
            Salidas.Add(new FilaSalida
            {
                Id               = rdrSalProy.GetInt32(0),
                CodFab           = rdrSalProy.GetString(1),
                CodInt           = rdrSalProy.GetString(2),
                Descripcion      = rdrSalProy.GetString(3),
                Cantidad         = rdrSalProy.GetDecimal(4),
                Um               = rdrSalProy.GetString(5),
                FechaSalida      = rdrSalProy.GetDateTime(6),
                NoSalida         = rdrSalProy.IsDBNull(7)  ? null : rdrSalProy.GetString(7),
                Recibe           = rdrSalProy.GetString(8),
                Instalado        = rdrSalProy.GetString(9),
                Balance          = rdrSalProy.GetDecimal(10),
                ProyectoAsignado = rdrSalProy.GetString(11),
                Obs              = rdrSalProy.GetString(12),
            });
        }

        return Page();
    }

    private async Task CargarEncabezadoAsync(SqlConnection conn, string folio)
    {
        var sql = @"
            SELECT TOP 1 ISNULL(folio, '') AS folio
            FROM inventario.cotizaciones
            WHERE folio = @p
            ORDER BY id DESC;";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 200).Value = folio;
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
        {
            Ot = rdr.GetString(0);
        }
    }

    public class FilaSalida
    {
        public int      Id               { get; set; }
        public string   CodFab           { get; set; } = "";
        public string   CodInt           { get; set; } = "";
        public string   Descripcion      { get; set; } = "";
        public decimal  Cantidad         { get; set; }
        public string   Um               { get; set; } = "";
        public DateTime FechaSalida      { get; set; }
        public string?  NoSalida         { get; set; }
        public string   Recibe           { get; set; } = "";
        public string   Instalado        { get; set; } = "";
        public decimal  Balance          { get; set; }
        public string   ProyectoAsignado { get; set; } = "";
        public string   Obs              { get; set; } = "";
    }
}
