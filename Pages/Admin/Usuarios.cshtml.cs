using InventarioWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages.Admin;

public class UsuariosModel : PageBase
{
    private readonly IConfiguration _config;
    private readonly AuthService _auth;

    public UsuariosModel(IConfiguration config, AuthService auth)
    {
        _config = config;
        _auth   = auth;
    }

    public List<UsuarioRow> Usuarios { get; private set; } = new();

    // Módulos disponibles en el sistema
    public static readonly string[] ModulosDisponibles =
        ["inventario", "cotizaciones", "compras", "admin"];

    // ── Cargar lista de usuarios ──────────────────────────────
    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("admin");
        if (deny != null) return deny;

        await CargarUsuariosAsync();
        return Page();
    }

    // ── Crear nuevo usuario ───────────────────────────────────
    public async Task<IActionResult> OnPostCrearAsync(
        string nombre, string usuario, string password,
        List<string> modulos)
    {
        var deny = VerificarAcceso("admin");
        if (deny != null) return deny;

        if (string.IsNullOrWhiteSpace(nombre) ||
            string.IsNullOrWhiteSpace(usuario) ||
            string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "Todos los campos son obligatorios.";
            return RedirectToPage();
        }

        if (password.Length < 6)
        {
            TempData["Error"] = "La contraseña debe tener al menos 6 caracteres.";
            return RedirectToPage();
        }

        var hash    = _auth.HashPassword(password);
        var connStr = _config.GetConnectionString("SqlServer");

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Insertar usuario
        var sql = @"INSERT INTO inventario.usuarios (nombre_completo, usuario, password_hash)
                    OUTPUT INSERTED.id
                    VALUES (@nombre, @usuario, @hash)";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@nombre",  SqlDbType.NVarChar, 150).Value = nombre.Trim();
        cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value  = usuario.Trim().ToLower();
        cmd.Parameters.Add("@hash",    SqlDbType.NVarChar, 256).Value = hash;

        int nuevoId;
        try
        {
            nuevoId = (int)(await cmd.ExecuteScalarAsync())!;
        }
        catch
        {
            TempData["Error"] = "El nombre de usuario ya existe.";
            return RedirectToPage();
        }

        // Insertar permisos
        foreach (var modulo in modulos)
        {
            var sqlP = @"INSERT INTO inventario.usuarios_permisos (id_usuario, modulo)
                         VALUES (@id, @modulo)";
            await using var cmdP = new SqlCommand(sqlP, conn);
            cmdP.Parameters.Add("@id",     SqlDbType.Int).Value          = nuevoId;
            cmdP.Parameters.Add("@modulo", SqlDbType.NVarChar, 50).Value = modulo;
            await cmdP.ExecuteNonQueryAsync();
        }

        TempData["Exito"] = $"Usuario '{usuario}' creado correctamente.";
        return RedirectToPage();
    }

    // ── Activar / Desactivar usuario ──────────────────────────
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var deny = VerificarAcceso("admin");
        if (deny != null) return deny;

        // No puedes desactivarte a ti mismo
        if (UsuarioActual?.Id == id)
        {
            TempData["Error"] = "No puedes desactivar tu propia cuenta.";
            return RedirectToPage();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"UPDATE inventario.usuarios
                    SET activo = CASE WHEN activo = 1 THEN 0 ELSE 1 END
                    WHERE id = @id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await cmd.ExecuteNonQueryAsync();

        return RedirectToPage();
    }

    // ── Editar módulos de un usuario ──────────────────────────
    public async Task<IActionResult> OnPostEditarModulosAsync(int id, List<string> modulos)
    {
        var deny = VerificarAcceso("admin");
        if (deny != null) return deny;

        // No puedes editar tus propios módulos
        if (UsuarioActual?.Id == id)
        {
            TempData["Error"] = "No puedes editar tus propios módulos.";
            return RedirectToPage();
        }

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Borrar permisos actuales y reinsertar los nuevos
        var sqlDel = "DELETE FROM inventario.usuarios_permisos WHERE id_usuario = @id";
        await using var cmdDel = new SqlCommand(sqlDel, conn);
        cmdDel.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await cmdDel.ExecuteNonQueryAsync();

        foreach (var modulo in modulos)
        {
            var sqlIns = @"INSERT INTO inventario.usuarios_permisos (id_usuario, modulo)
                           VALUES (@id, @modulo)";
            await using var cmdIns = new SqlCommand(sqlIns, conn);
            cmdIns.Parameters.Add("@id",     SqlDbType.Int).Value          = id;
            cmdIns.Parameters.Add("@modulo", SqlDbType.NVarChar, 50).Value = modulo;
            await cmdIns.ExecuteNonQueryAsync();
        }

        TempData["Exito"] = "Módulos actualizados correctamente.";
        return RedirectToPage();
    }

    // ── Restablecer contraseña ────────────────────────────────
    public async Task<IActionResult> OnPostCambiarPasswordAsync(int id, string nuevaPassword, string confirmarPassword)
    {
        var deny = VerificarAcceso("admin");
        if (deny != null) return deny;

        if (UsuarioActual?.Id == id)
        {
            TempData["Error"] = "No puedes restablecer tu propia contraseña desde aquí.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(nuevaPassword))
        {
            TempData["Error"] = "La contraseña no puede estar vacía.";
            return RedirectToPage();
        }

        if (nuevaPassword.Length < 6)
        {
            TempData["Error"] = "La contraseña debe tener al menos 6 caracteres.";
            return RedirectToPage();
        }

        if (nuevaPassword != confirmarPassword)
        {
            TempData["Error"] = "Las contraseñas no coinciden.";
            return RedirectToPage();
        }

        var hash    = _auth.HashPassword(nuevaPassword);
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "UPDATE inventario.usuarios SET password_hash = @hash WHERE id = @id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 256).Value = hash;
        cmd.Parameters.Add("@id",   SqlDbType.Int).Value           = id;
        await cmd.ExecuteNonQueryAsync();

        TempData["Exito"] = "Contraseña restablecida correctamente.";
        return RedirectToPage();
    }

    private async Task CargarUsuariosAsync()
    {
        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"SELECT u.id, u.nombre_completo, u.usuario, u.activo,
                           STRING_AGG(p.modulo, ',') AS modulos
                    FROM inventario.usuarios u
                    LEFT JOIN inventario.usuarios_permisos p ON p.id_usuario = u.id
                    GROUP BY u.id, u.nombre_completo, u.usuario, u.activo
                    ORDER BY u.id";

        await using var cmd = new SqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();

        while (await rdr.ReadAsync())
        {
            var modulosStr = rdr.IsDBNull(4) ? "" : rdr.GetString(4);
            Usuarios.Add(new UsuarioRow
            {
                Id      = rdr.GetInt32(0),
                Nombre  = rdr.GetString(1),
                Usuario = rdr.GetString(2),
                Activo  = rdr.GetBoolean(3),
                Modulos = modulosStr.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            });
        }
    }

    public class UsuarioRow
    {
        public int             Id      { get; set; }
        public string          Nombre  { get; set; } = "";
        public string          Usuario { get; set; } = "";
        public bool            Activo  { get; set; }
        public HashSet<string> Modulos { get; set; } = new();
    }
}