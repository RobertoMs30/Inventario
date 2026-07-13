using InventarioWeb.Services;
using Xunit;

namespace InventarioWeb.Tests;

// Pruebas de la lógica de permisos por módulo de UsuarioSesion.
// Pura lógica en memoria: sin base de datos, sin efectos secundarios.
public class UsuarioSesionTests
{
    [Fact]
    public void Tiene_DevuelveTrue_CuandoElUsuarioTieneEseModulo()
    {
        var u = new UsuarioSesion { Modulos = new() { "inventario", "compras" } };

        Assert.True(u.Tiene("inventario"));
        Assert.True(u.Tiene("compras"));
    }

    [Fact]
    public void Tiene_DevuelveFalse_CuandoElUsuarioNoTieneEseModulo()
    {
        var u = new UsuarioSesion { Modulos = new() { "inventario" } };

        Assert.False(u.Tiene("compras"));
    }

    [Fact]
    public void EsAdmin_DevuelveTrue_SoloSiTieneElModuloAdmin()
    {
        var admin = new UsuarioSesion { Modulos = new() { "admin" } };
        var normal = new UsuarioSesion { Modulos = new() { "inventario" } };

        Assert.True(admin.EsAdmin);
        Assert.False(normal.EsAdmin);
    }

    [Fact]
    public void Admin_TieneAccesoATodosLosModulos()
    {
        var admin = new UsuarioSesion { Modulos = new() { "admin" } };

        // Un admin pasa Tiene() para cualquier módulo, aunque no esté en su lista.
        Assert.True(admin.Tiene("inventario"));
        Assert.True(admin.Tiene("compras"));
        Assert.True(admin.Tiene("cualquier_modulo_nuevo"));
    }

    [Fact]
    public void Usuario_SinModulos_NoTieneAcceso()
    {
        var u = new UsuarioSesion();

        Assert.False(u.EsAdmin);
        Assert.False(u.Tiene("inventario"));
    }
}
