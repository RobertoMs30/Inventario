-- Agregar columna proyecto_asignado a salidas_inventario
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.salidas_inventario') AND name = 'proyecto_asignado'
)
BEGIN
    ALTER TABLE inventario.salidas_inventario
        ADD proyecto_asignado NVARCHAR(200) NULL;
    PRINT 'Columna proyecto_asignado agregada.';
END
ELSE
    PRINT 'La columna proyecto_asignado ya existe.';
GO

-- Recrear SP con el nuevo parametro
ALTER PROCEDURE dbo.sp_RegistrarSalidaMaterial
    @cod_fab           NVARCHAR(100),
    @cod_int           NVARCHAR(100),
    @descripcion       NVARCHAR(500),
    @cantidad          DECIMAL(18,2),
    @um                NVARCHAR(50),
    @fecha_salida      DATETIME,
    @no_salida         NVARCHAR(100) = NULL,
    @recibe            NVARCHAR(200),
    @instalado         NVARCHAR(300),
    @obs               NVARCHAR(500) = NULL,
    @proyecto_asignado NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) Verificar que el material existe
    IF NOT EXISTS (
        SELECT 1 FROM inventario.catalogo_materiales
        WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab))
    )
    BEGIN
        RAISERROR('El material con cod_fab ''%s'' no existe en el catálogo.', 16, 1, @cod_fab);
        RETURN;
    END

    -- 2) Verificar stock suficiente
    DECLARE @cant_actual DECIMAL(18,2);
    SELECT @cant_actual = cant
    FROM inventario.catalogo_materiales
    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

    IF @cant_actual < @cantidad
    BEGIN
        DECLARE @msg NVARCHAR(500) =
            'Stock insuficiente. Existencia actual: ' + CAST(@cant_actual AS NVARCHAR(50))
            + ', cantidad solicitada: ' + CAST(@cantidad AS NVARCHAR(50)) + '.';
        RAISERROR(@msg, 16, 1);
        RETURN;
    END

    -- 3) Calcular balance
    DECLARE @balance DECIMAL(18,2) = @cant_actual - @cantidad;

    -- 4) Insertar y actualizar en una transaccion
    BEGIN TRANSACTION;
    BEGIN TRY
        INSERT INTO inventario.salidas_inventario
            (cod_fab, cod_int, descripcion, cantidad, um, fecha_salida,
             no_salida, recibe, instalado, balance, obs, proyecto_asignado)
        VALUES
            (LTRIM(RTRIM(@cod_fab)), LTRIM(RTRIM(@cod_int)), @descripcion,
             @cantidad, LTRIM(RTRIM(@um)), @fecha_salida,
             @no_salida, @recibe, @instalado, @balance, @obs, @proyecto_asignado);

        UPDATE inventario.catalogo_materiales
        SET cant = @balance
        WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

PRINT 'SP sp_RegistrarSalidaMaterial actualizado correctamente.';
