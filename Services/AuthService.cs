using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.SqlTypes;
using System.Security.Cryptography;

namespace InventarioWeb.Services;

public class AuthService
{
    private readonly IConfiguration _config;
 
    public AuthService (IConfiguration config)
    {
        _config = config;
    }

    public string HashPassword(string password)
    {
    byte[] salt = RandomNumberGenerator.GetBytes(16);

    byte[] hash = KeyDerivation.Pbkdf2(
        password:       password,
        salt:           salt,
        prf:            KeyDerivationPrf.HMACSHA256,
        iterationCount: 100_000,
        numBytesRequested: 32);

    byte[] resultado = new byte[48];
    salt.CopyTo(resultado, 0);
    hash.CopyTo(resultado, 16);
    return Convert.ToBase64String(resultado);
    }

public bool VerificarPassword(string password, string hashGuardado)
    {
        byte[] datos = Convert.FromBase64String(hashGuardado);

        byte[] salt = datos[..16];

        byte[] hash = KeyDerivation.Pbkdf2(
        password:       password,
        salt:           salt,
        prf:            KeyDerivationPrf.HMACSHA256,
        iterationCount: 100_000,
        numBytesRequested: 32);

        byte[] hashGuardadoBytes = datos[16..];
        return CryptographicOperations.FixedTimeEquals(hash,hashGuardadoBytes);
    }

public async Task<UsuarioSesion?> LoginAsync(string usuario, string password)
    {
        var connstr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connstr);
        await conn.OpenAsync ();

        var sql = @"SELECT u.id, u.nombre_completo, u.password_hash,
                           STRING_AGG(p.modulo, ',') AS modulos
                    From inventario.usuarios u
                    LEFT JOIN inventario.usuarios_permisos p ON p.id_usuario = u.id
                    WHERE u.usuario = @usuario AND u.activo = 1
                    GROUP BY u.id, u.nombre_completo, u.password_hash";
        
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value = usuario.Trim();

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync ()) return null; //en caso de que el user no exista

        var hashGuardado = rdr.GetString(2);
        if (!VerificarPassword(password, hashGuardado)) return null; //password incorrecto

        return new UsuarioSesion
        {
            Id      = rdr.GetInt32(0),
            Nombre  = rdr.GetString(1),
            Modulos = rdr.IsDBNull(3) ? [] : rdr.GetString(3).Split(',').ToHashSet()
        };

    }
    
}

public class UsuarioSesion
{
    public int          Id      {get; set; }
    public string       Nombre  {get; set; } = "";
    public HashSet<string> Modulos {get; set; } = [];

    public bool EsAdmin => Modulos.Contains("admin");
    public bool Tiene(string modulo) => EsAdmin || Modulos.Contains(modulo);

}