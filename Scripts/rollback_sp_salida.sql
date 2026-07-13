-- ============================================================
--  ROLLBACK: restaura sp_RegistrarSalidaMaterial a la versión
--  ORIGINAL de producción (sin transacción, sin validación).
--  Úsalo SOLO si necesitas revertir fix_sp_salida_concurrencia.sql.
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.sp_RegistrarSalidaMaterial
    @cod_fab           NVARCHAR(200),
    @cod_int           NVARCHAR(200),
    @descripcion       NVARCHAR(MAX),
    @cantidad          DECIMAL(18,2),
    @um                NVARCHAR(50),
    @fecha_salida      DATE,
    @no_salida         NVARCHAR(100) = NULL,
    @recibe            NVARCHAR(200),
    @instalado         NVARCHAR(200),
    @obs               NVARCHAR(MAX) = NULL,
    @proyecto_asignado NVARCHAR(200) = NULL,
    @responsable       NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @balance DECIMAL(18,2);

    SELECT @balance = ISNULL(cant, 0) - @cantidad
    FROM inventario.catalogo_materiales
    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

    UPDATE inventario.catalogo_materiales
    SET cant = ISNULL(cant, 0) - @cantidad
    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

    INSERT INTO inventario.salidas_inventario
        (cod_fab, cod_int, descripcion, cantidad, um, fecha_salida,
         no_salida, recibe, instalado, balance, obs,
         proyecto_asignado, responsable)
    VALUES
        (@cod_fab, @cod_int, @descripcion, @cantidad, @um, @fecha_salida,
         @no_salida, @recibe, @instalado, ISNULL(@balance, 0), @obs,
         @proyecto_asignado, @responsable)
END
