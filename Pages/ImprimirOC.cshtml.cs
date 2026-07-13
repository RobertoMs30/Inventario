using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace InventarioWeb.Pages;

public class ImprimirOCModel : PageBase
{
    private readonly IConfiguration _config;
    public ImprimirOCModel(IConfiguration config) { _config = config; }

    public OrdenCabecera? Orden { get; private set; }
    public List<OrdenLinea> Lineas { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deny = VerificarAcceso("compras");
        if (deny != null) return deny;

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // Cabecera
        var sql = @"SELECT id, oc, proveedor, elaboro, fecha, proyecto,
                           ISNULL(num_requisicion,''),
                           ISNULL(reviso,''), ISNULL(aprobo,'')
                    FROM administracion_proyectos.ordenes_compra WHERE id = @id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return NotFound();

        Orden = new OrdenCabecera
        {
            Id             = rdr.GetInt32(0),
            Oc             = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
            Proveedor      = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
            Elaboro        = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
            Fecha          = rdr.IsDBNull(4) ? DateTime.Today : rdr.GetDateTime(4),
            Proyecto       = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
            NumRequisicion = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
            Reviso         = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
            Aprobo         = rdr.IsDBNull(8) ? "" : rdr.GetString(8),
        };
        await rdr.CloseAsync();

        // Buscar datos de contacto del proveedor y cliente
        await BuscarContactoProveedorAsync(conn, Orden);
        await BuscarDatosClienteAsync(conn, Orden);

        // Líneas
        var sqlDet = @"SELECT no_articulo, ISNULL(cod_material,''), ISNULL(descripcion,''),
                              ISNULL(cantidad,0), ISNULL(tipo_unidad,''),
                              ISNULL(precio_unitario,0), ISNULL(importe_total,0)
                       FROM administracion_proyectos.ordenes_compra_detalle
                       WHERE id_oc = @id ORDER BY no_articulo";
        await using var cmdD = new SqlCommand(sqlDet, conn);
        cmdD.Parameters.AddWithValue("@id", id);
        await using var rdrD = await cmdD.ExecuteReaderAsync();
        while (await rdrD.ReadAsync())
            Lineas.Add(new OrdenLinea
            {
                NoArticulo     = rdrD.GetInt32(0),
                CodMaterial    = rdrD.GetString(1),
                Descripcion    = rdrD.GetString(2),
                Cantidad       = rdrD.GetDecimal(3),
                TipoUnidad     = rdrD.GetString(4),
                PrecioUnitario = rdrD.GetDecimal(5),
                ImporteTotal   = rdrD.GetDecimal(6),
            });

        return Page();
    }

    // Busca cliente y dirección de consigna desde la tabla de proyectos
    private async Task BuscarDatosClienteAsync(SqlConnection conn, OrdenCabecera orden)
    {
        if (string.IsNullOrWhiteSpace(orden.Proyecto)) return;
        try
        {
            // Detectar columnas disponibles en la tabla de proyectos
            var sqlCols = @"SELECT name FROM sys.columns
                            WHERE object_id = OBJECT_ID('administracion_proyectos.proyectos')
                              AND name IN ('cliente','calle','numero_ext','num_ext','colonia','cp','codigo_postal')";
            await using var cmdCols = new SqlCommand(sqlCols, conn);
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var rdrCols = await cmdCols.ExecuteReaderAsync();
            while (await rdrCols.ReadAsync()) cols.Add(rdrCols.GetString(0));
            await rdrCols.CloseAsync();

            if (!cols.Contains("cliente")) return;

            // CAST a NVARCHAR para que la lectura con GetString no falle si la
            // columna es numérica (p. ej. numero_ext o cp guardados como int)
            string numExtCol = cols.Contains("numero_ext") ? "ISNULL(CAST(numero_ext AS NVARCHAR(50)),'')" :
                               cols.Contains("num_ext")    ? "ISNULL(CAST(num_ext AS NVARCHAR(50)),'')"    : "''";
            string cpCol     = cols.Contains("cp")             ? "ISNULL(CAST(cp AS NVARCHAR(20)),'')"             :
                               cols.Contains("codigo_postal")  ? "ISNULL(CAST(codigo_postal AS NVARCHAR(20)),'')"  : "''";
            string calleCol  = cols.Contains("calle")   ? "ISNULL(CAST(calle AS NVARCHAR(200)),'')"   : "''";
            string coloniaCol= cols.Contains("colonia") ? "ISNULL(CAST(colonia AS NVARCHAR(200)),'')" : "''";

            // El proyecto puede venir como "12345 - Nombre proyecto" o solo el número
            var proyNum = orden.Proyecto.Split('-')[0].Trim();
            var sqlProj = $@"SELECT TOP 1 ISNULL(CAST(cliente AS NVARCHAR(200)),''), {calleCol}, {numExtCol}, {coloniaCol}, {cpCol}
                             FROM administracion_proyectos.proyectos
                             WHERE CAST(cotizacion AS NVARCHAR(50)) = @proy";
            await using var cmdProj = new SqlCommand(sqlProj, conn);
            cmdProj.Parameters.Add("@proy", SqlDbType.NVarChar, 50).Value = proyNum;
            await using var rdrProj = await cmdProj.ExecuteReaderAsync();
            if (await rdrProj.ReadAsync())
            {
                orden.Cliente   = rdrProj.IsDBNull(0) ? "" : rdrProj.GetString(0);
                orden.Calle     = rdrProj.IsDBNull(1) ? "" : rdrProj.GetString(1);
                orden.NumeroExt = rdrProj.IsDBNull(2) ? "" : rdrProj.GetString(2);
                orden.Colonia   = rdrProj.IsDBNull(3) ? "" : rdrProj.GetString(3);
                orden.Cp        = rdrProj.IsDBNull(4) ? "" : rdrProj.GetString(4);
            }
        }
        catch { /* Si las columnas no existen, se dejan vacíos */ }
    }

    // Busca contacto/teléfono/email en inventario.cat_proveedores
    private async Task BuscarContactoProveedorAsync(SqlConnection conn, OrdenCabecera orden)
    {
        if (string.IsNullOrWhiteSpace(orden.Proveedor)) return;
        try
        {
            // Detectar qué columnas existen en la tabla
            var sqlCols = @"SELECT name FROM sys.columns
                            WHERE object_id = OBJECT_ID('inventario.cat_proveedores')
                              AND name IN ('contacto','telefono','telefono1','telefono2',
                                          'tel','tel1','tel2','email','correo',
                                          'nombre','razon_social','proveedor','nombre_comercial')";
            await using var cmdCols = new SqlCommand(sqlCols, conn);
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var rdrCols = await cmdCols.ExecuteReaderAsync();
            while (await rdrCols.ReadAsync()) cols.Add(rdrCols.GetString(0));
            await rdrCols.CloseAsync();

            if (cols.Count == 0) return; // tabla no existe

            string contactoCol = cols.Contains("contacto")       ? "ISNULL(contacto,'')"       : "''";
            string telCol      = cols.Contains("telefono1")      ? "ISNULL(telefono1,'')"      :
                                 cols.Contains("telefono")       ? "ISNULL(telefono,'')"       :
                                 cols.Contains("tel1")           ? "ISNULL(tel1,'')"           :
                                 cols.Contains("tel")            ? "ISNULL(tel,'')"            : "''";
            string tel2Col     = cols.Contains("telefono2")      ? "ISNULL(telefono2,'')"      :
                                 cols.Contains("tel2")           ? "ISNULL(tel2,'')"           : "''";
            string emailCol    = cols.Contains("email")          ? "ISNULL(email,'')"          :
                                 cols.Contains("correo")         ? "ISNULL(correo,'')"         : "''";
            string nombreCol   = cols.Contains("nombre_comercial")? "nombre_comercial"         :
                                 cols.Contains("nombre")         ? "nombre"                    :
                                 cols.Contains("razon_social")   ? "razon_social"              :
                                 cols.Contains("proveedor")      ? "proveedor"                 : "nombre";

            var sqlProv = $@"SELECT TOP 1 {contactoCol}, {telCol}, {tel2Col}, {emailCol}
                             FROM inventario.cat_proveedores
                             WHERE {nombreCol} LIKE '%' + @prov + '%'";
            await using var cmdProv = new SqlCommand(sqlProv, conn);
            cmdProv.Parameters.Add("@prov", SqlDbType.NVarChar, 300).Value = orden.Proveedor.Trim();
            await using var rdrProv = await cmdProv.ExecuteReaderAsync();
            if (await rdrProv.ReadAsync())
            {
                orden.Contacto = rdrProv.IsDBNull(0) ? "" : rdrProv.GetString(0);
                var tel1       = rdrProv.IsDBNull(1) ? "" : rdrProv.GetString(1);
                var tel2       = rdrProv.IsDBNull(2) ? "" : rdrProv.GetString(2);
                orden.Telefono = !string.IsNullOrEmpty(tel1) ? tel1 : tel2;
                orden.Email    = rdrProv.IsDBNull(3) ? "" : rdrProv.GetString(3);
            }
        }
        catch { /* Si la tabla/columnas no coinciden, se dejan vacíos */ }
    }

    public class OrdenCabecera
    {
        public int      Id             { get; set; }
        public string   Oc             { get; set; } = "";
        public string   Proveedor      { get; set; } = "";
        public string   Elaboro        { get; set; } = "";
        public DateTime Fecha          { get; set; }
        public string   Proyecto       { get; set; } = "";
        public string   NumRequisicion { get; set; } = "";
        public string   Contacto       { get; set; } = "";
        public string   Telefono       { get; set; } = "";
        public string   Email          { get; set; } = "";
        public string   Reviso         { get; set; } = "";
        public string   Aprobo         { get; set; } = "";
        public string   Cliente        { get; set; } = "";
        public string   Calle          { get; set; } = "";
        public string   NumeroExt      { get; set; } = "";
        public string   Colonia        { get; set; } = "";
        public string   Cp             { get; set; } = "";
    }

    public class OrdenLinea
    {
        public int     NoArticulo     { get; set; }
        public string  CodMaterial    { get; set; } = "";
        public string  Descripcion    { get; set; } = "";
        public decimal Cantidad       { get; set; }
        public string  TipoUnidad     { get; set; } = "";
        public decimal PrecioUnitario { get; set; }
        public decimal ImporteTotal   { get; set; }
    }
}
