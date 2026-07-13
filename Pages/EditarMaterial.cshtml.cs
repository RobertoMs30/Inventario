using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.ComponentModel.DataAnnotations;

namespace InventarioWeb.Pages;

public class EditarMaterialModel : PageBase
{
    private readonly IConfiguration _config;
    public EditarMaterialModel(IConfiguration config) => _config = config;

    [BindProperty(SupportsGet = true)]
    [Required]
    public string CodFab { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(300, ErrorMessage = "La descripción no puede pasar de 300 caracteres.")]
    public string Descripcion { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "UM es obligatoria.")]
    [StringLength(40, ErrorMessage = "UM no puede pasar de 40 caracteres.")]
    public string Um { get; set; } = "";

    [BindProperty]
    [StringLength(200, ErrorMessage = "Proveedor no puede pasar de 200 caracteres.")]
    public string Proveedor { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Partida es obligatoria.")]
    [StringLength(200, ErrorMessage = "Partida no puede pasar de 200 caracteres.")]
    public string Partida { get; set; } = "";

    [BindProperty]
    [Range(0, 999999999, ErrorMessage = "PU debe ser 0 o mayor.")]
    public decimal? Pu { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Moneda es obligatoria.")]
    [RegularExpression("^(MXN|USD)$", ErrorMessage = "Moneda debe ser MXN o USD.")]
    public string Moneda { get; set; } = "MXN";

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        CodFab = (CodFab ?? "").Trim();
        if (string.IsNullOrWhiteSpace(CodFab))
            return RedirectToPage("/Catalogo");

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
SELECT TOP 1 cod_fab, descripcion, um, proveedor, partida, pu, moneda
FROM inventario.catalogo_materiales
WHERE LTRIM(RTRIM(cod_fab)) = @cod_fab;
";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cod_fab", CodFab);

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync())
            return RedirectToPage("/Catalogo");

        Descripcion = rdr["descripcion"]?.ToString() ?? "";
        Um = rdr["um"]?.ToString() ?? "";
        Proveedor = rdr["proveedor"]?.ToString() ?? "";
        Partida = rdr["partida"]?.ToString() ?? "";
        Pu = rdr["pu"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rdr["pu"]);
        Moneda = rdr["moneda"]?.ToString() ?? "MXN";

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deny = VerificarAcceso("inventario");
        if (deny != null) return deny;

        // Validación
        CodFab = (CodFab ?? "").Trim();
        Descripcion = (Descripcion ?? "").Trim();
        Um = (Um ?? "").Trim();
        Proveedor = (Proveedor ?? "").Trim();
        Partida = (Partida ?? "").Trim();
        Moneda = (Moneda ?? "MXN").Trim();

        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrWhiteSpace(CodFab))
        {
            ModelState.AddModelError(string.Empty, "CodFab llegó vacío. No se pudo guardar.");
            return Page();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_UpdateCatalogoMaterial", conn);
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.AddWithValue("@Cod_fab", CodFab);
        cmd.Parameters.AddWithValue("@Descripcion", Descripcion);
        cmd.Parameters.AddWithValue("@Um", Um);
        cmd.Parameters.AddWithValue("@Proveedor", Proveedor);
        cmd.Parameters.AddWithValue("@Partida", Partida);
        cmd.Parameters.AddWithValue("@Pu", (object?)Pu ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Moneda", Moneda);

        await cmd.ExecuteNonQueryAsync();

        // ✅ Regresa al catálogo filtrado para que VEAS el cambio luego luego
        return RedirectToPage("/Catalogo", new { q = CodFab, saved = 1 });
    }
}

