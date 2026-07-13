-- ============================================================
--  CORRECCIÓN: sp_RegistrarSalidaMaterial
--  Hace ATÓMICO el descuento de stock + el registro de salida,
--  y bloquea la fila del material para evitar lecturas
--  simultáneas. NO cambia el resultado normal de una salida.
--
--  ⚠️  NO ejecutar en producción sin RESPALDO previo de la BD.
--      Usa CREATE OR ALTER: reemplaza el SP existente.
--      Recomendado: ejecutar primero en un entorno de prueba.
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
    SET XACT_ABORT ON;            -- ante cualquier error, rollback garantizado

    DECLARE @cant_actual DECIMAL(18,2);
    DECLARE @balance     DECIMAL(18,2);

    BEGIN TRY
        BEGIN TRAN;

        -- Bloquea la fila del material durante la transacción para que
        -- dos salidas simultáneas no lean el mismo stock a la vez.
        SELECT @cant_actual = ISNULL(cant, 0)
        FROM inventario.catalogo_materiales WITH (UPDLOCK, HOLDLOCK)
        WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

        -- Si el material no existe en catálogo, @cant_actual queda NULL.
        IF @cant_actual IS NULL
            THROW 50010, 'El material no existe en el catálogo.', 1;

        -- Bloquea sobreventa: si no hay existencia suficiente, se
        -- rechaza la salida y el mensaje se muestra al usuario.
        IF @cant_actual < @cantidad
            THROW 50011, 'Stock insuficiente para la salida.', 1;

        SET @balance = @cant_actual - @cantidad;

        UPDATE inventario.catalogo_materiales
        SET cant = @balance
        WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

        INSERT INTO inventario.salidas_inventario
            (cod_fab, cod_int, descripcion, cantidad, um, fecha_salida,
             no_salida, recibe, instalado, balance, obs,
             proyecto_asignado, responsable)
        VALUES
            (@cod_fab, @cod_int, @descripcion, @cantidad, @um, @fecha_salida,
             @no_salida, @recibe, @instalado, @balance, @obs,
             @proyecto_asignado, @responsable);

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;     -- propaga el error a la app (ya lo captura y lo muestra)
    END CATCH
END
