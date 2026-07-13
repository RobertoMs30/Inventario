using InventarioWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace InventarioWeb.Pages;

public abstract class PageBase : PageModel
{
    // ── Clave con la que guardamos el usuario en sesión ───────
    private const string SesionKey = "usuario_sesion";

    // ── Usuario actual (null si no hay sesión) ────────────────
    public UsuarioSesion? UsuarioActual { get; private set; }

    // ── Nombre para mostrar en el layout ─────────────────────
    public string NombreUsuario => UsuarioActual?.Nombre ?? "";

    // ── ¿Está activada la verificación de acceso? ─────────────
    private bool AuthEnabled => HttpContext.RequestServices
        .GetRequiredService<IConfiguration>()
        .GetValue<bool>("AuthEnabled");

    // ── Carga el usuario de la sesión en UsuarioActual ────────
    // Devuelve true si hay sesión válida; false si no.
    private bool CargarUsuarioDeSesion()
    {
        var json = HttpContext.Session.GetString(SesionKey);
        if (string.IsNullOrEmpty(json)) return false;
        UsuarioActual = JsonSerializer.Deserialize<UsuarioSesion>(json);
        return UsuarioActual is not null;
    }

    // ── Solo exige sesión iniciada, sin requerir un módulo ────
    // Útil para páginas accesibles a cualquier usuario válido (ej. dashboard).
    // null = todo bien; si no, redirige a Login.
    protected IActionResult? VerificarSesion()
    {
        if (!AuthEnabled) return null;
        if (!CargarUsuarioDeSesion()) return RedirectToPage("/Login");
        return null;
    }

    // ── Llama esto en el OnGet/OnPost de cada page ────────────
    // modulo: el nombre del módulo que protege esa página
    // null = todo bien; si no, redirige a Login o SinAcceso.
    protected IActionResult? VerificarAcceso(string modulo)
    {
        if (!AuthEnabled) return null;
        if (!CargarUsuarioDeSesion()) return RedirectToPage("/Login");
        if (!UsuarioActual!.Tiene(modulo)) return RedirectToPage("/SinAcceso");
        return null; // null = todo bien, puede continuar
    }

    // ── Versión para handlers AJAX que devuelven JSON ─────────
    // Responde 403 con JSON en vez de redirigir (un redirect rompería el fetch).
    // null = todo bien; si no, un JsonResult con código 403.
    protected IActionResult? VerificarAccesoJson(string modulo)
    {
        if (!AuthEnabled) return null;
        if (!CargarUsuarioDeSesion() || !UsuarioActual!.Tiene(modulo))
            return new JsonResult(new { ok = false, error = "Sin acceso" }) { StatusCode = 403 };
        return null;
    }

    // ── Guardar usuario en sesión (se llama al hacer login) ───
    protected void GuardarSesion(UsuarioSesion usuario)
    {
        var json = JsonSerializer.Serialize(usuario);
        HttpContext.Session.SetString(SesionKey, json);
    }

    // ── Limpiar sesión (se llama al hacer logout) ─────────────
    protected void CerrarSesion()
    {
        HttpContext.Session.Clear();
    }
}