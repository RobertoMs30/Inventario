using InventarioWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class SetupModel : PageModel
{
    private readonly AuthService _auth;
    private readonly IConfiguration _config;

    public SetupModel(AuthService auth, IConfiguration config)
    {
        _auth = auth;
        _config = config;
    }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Si ya hay usuarios, esta página no debe existir
        if (await HayUsuariosAsync())
            return RedirectToPage("/Login");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string nombre, string usuario, string password)
    {
        // Doble verificación: si ya hay usuarios, rechazar
        if (await HayUsuariosAsync())
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(nombre) ||
            string.IsNullOrWhiteSpace(usuario) ||
            string.IsNullOrWhiteSpace(password))
        {
            Error = "Todos los campos son obligatorios.";
            return Page();
        }

        if (password.Length < 6)
        {
            Error = "La contraseña debe tener al menos 6 caracteres.";
            return Page();
        }

        var hash = _auth.HashPassword(password);

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Insertar usuario admin
        var sqlUsuario = @"INSERT INTO inventario.usuarios (nombre_completo, usuario, password_hash)
                           OUTPUT INSERTED.id
                           VALUES (@nombre, @usuario, @hash)";
        await using var cmd = new SqlCommand(sqlUsuario, conn);
        cmd.Parameters.Add("@nombre",  SqlDbType.NVarChar, 150).Value = nombre.Trim();
        cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value  = usuario.Trim().ToLower();
        cmd.Parameters.Add("@hash",    SqlDbType.NVarChar, 256).Value = hash;

        var nuevoId = (int)(await cmd.ExecuteScalarAsync())!;

        // Darle permiso de admin (acceso total)
        var sqlPermiso = @"INSERT INTO inventario.usuarios_permisos (id_usuario, modulo)
                           VALUES (@id, 'admin')";
        await using var cmdP = new SqlCommand(sqlPermiso, conn);
        cmdP.Parameters.Add("@id", SqlDbType.Int).Value = nuevoId;
        await cmdP.ExecuteNonQueryAsync();

        return RedirectToPage("/Login");
    }

    private async Task<bool> HayUsuariosAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN EXISTS(SELECT 1 FROM inventario.usuarios) THEN 1 ELSE 0 END", conn);
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }
}
