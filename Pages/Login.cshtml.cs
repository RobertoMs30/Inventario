using InventarioWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace InventarioWeb.Pages;

public class LoginModel : PageModel
{
    private readonly AuthService _auth;
    private const string SesionKey = "usuario_sesion";

    public LoginModel(AuthService auth)
    {
        _auth = auth;
    }

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        // Si ya tiene sesión activa, mandarlo al inicio
        if (HttpContext.Session.GetString(SesionKey) != null)
            return RedirectToPage("/Index");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string usuario, string password)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
        {
            Error = "Ingresa usuario y contraseña.";
            return Page();
        }

        var sesion = await _auth.LoginAsync(usuario, password);

        if (sesion is null)
        {
            Error = "Usuario o contraseña incorrectos.";
            return Page();
        }

        var json = JsonSerializer.Serialize(sesion);
        HttpContext.Session.SetString(SesionKey, json);

        return RedirectToPage("/Index");
    }
}
