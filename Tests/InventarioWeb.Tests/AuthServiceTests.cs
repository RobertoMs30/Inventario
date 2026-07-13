using InventarioWeb.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace InventarioWeb.Tests;

// Pruebas de la lógica de hasheo y verificación de contraseñas.
// Son funciones puras (PBKDF2): no tocan base de datos ni configuración,
// así que correrlas es 100% seguro y no afecta al sistema.
public class AuthServiceTests
{
    private static AuthService CrearServicio()
    {
        // AuthService pide IConfiguration en el constructor, pero HashPassword
        // y VerificarPassword no la usan. Pasamos una configuración vacía.
        var config = new ConfigurationBuilder().Build();
        return new AuthService(config);
    }

    [Fact]
    public void VerificarPassword_DevuelveTrue_ConContraseñaCorrecta()
    {
        var auth = CrearServicio();
        var hash = auth.HashPassword("MiClave123!");

        Assert.True(auth.VerificarPassword("MiClave123!", hash));
    }

    [Fact]
    public void VerificarPassword_DevuelveFalse_ConContraseñaIncorrecta()
    {
        var auth = CrearServicio();
        var hash = auth.HashPassword("MiClave123!");

        Assert.False(auth.VerificarPassword("claveEquivocada", hash));
    }

    [Fact]
    public void HashPassword_GeneraSaltAleatorio_HashesDistintosParaMismaClave()
    {
        var auth = CrearServicio();

        var hash1 = auth.HashPassword("clave");
        var hash2 = auth.HashPassword("clave");

        // Distinto salt => distinto hash, aunque la contraseña sea la misma.
        Assert.NotEqual(hash1, hash2);
        // Pero ambos deben verificar correctamente.
        Assert.True(auth.VerificarPassword("clave", hash1));
        Assert.True(auth.VerificarPassword("clave", hash2));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("contraseña con espacios")]
    [InlineData("ñÑáéíóú-acentos")]
    [InlineData("符号🔐emoji")]
    [InlineData("P@ssw0rd!#$%^&*()")]
    public void HashPassword_RoundTrip_FuncionaConCaracteresEspeciales(string clave)
    {
        var auth = CrearServicio();
        var hash = auth.HashPassword(clave);

        Assert.True(auth.VerificarPassword(clave, hash));
    }

    [Fact]
    public void VerificarPassword_DistingueMayusculas()
    {
        var auth = CrearServicio();
        var hash = auth.HashPassword("Clave");

        Assert.False(auth.VerificarPassword("clave", hash));
    }
}
