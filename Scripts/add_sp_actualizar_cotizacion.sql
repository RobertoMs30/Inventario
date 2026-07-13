-- ============================================================
--  Nuevo SP: sp_ActualizarCotizacion
--  Ejecutar en: TU_BASE_DE_DATOS
-- ============================================================

IF OBJECT_ID('dbo.sp_ActualizarCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ActualizarCotizacion;
GO

CREATE PROCEDURE dbo.sp_ActualizarCotizacion
    @id             INT,
    @fecha          DATE,
    @cliente        NVARCHAR(200),
    @proyecto       NVARCHAR(300),
    @items          NVARCHAR(MAX),   -- JSON array de ítems
    @rfq            NVARCHAR(100) = NULL,
    @descuento_pct  DECIMAL(5,2)  = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- 1) Verificar que la cotización existe
        IF NOT EXISTS (SELECT 1 FROM inventario.cotizaciones WHERE id = @id)
            THROW 50001, 'Cotización no encontrada.', 1;

        -- 2) Actualizar encabezado (el folio NO cambia)
        UPDATE inventario.cotizaciones
        SET fecha         = @fecha,
            cliente       = @cliente,
            proyecto      = @proyecto,
            rfq           = @rfq,
            descuento_pct = ISNULL(@descuento_pct, 0)
        WHERE id = @id;

        -- 3) Reemplazar todos los ítems existentes
        DELETE FROM inventario.cotizacion_items WHERE cotizacion_id = @id;

        INSERT INTO inventario.cotizacion_items
            (cotizacion_id, no_item, descripcion, cantidad, unidad, marca,
             mano_obra, precio_unitario, total, moneda, tiempo_entrega)
        SELECT
            @id,
            CAST(j.no_item          AS INT),
            j.descripcion,
            CAST(j.cantidad         AS DECIMAL(18,2)),
            j.unidad,
            j.marca,
            CAST(j.mano_obra        AS DECIMAL(18,2)),
            CAST(j.precio_unitario  AS DECIMAL(18,2)),
            CAST(j.total            AS DECIMAL(18,2)),
            j.moneda,
            j.tiempo_entrega
        FROM OPENJSON(@items)
        WITH (
            no_item         INT           '$.no_item',
            descripcion     NVARCHAR(500) '$.descripcion',
            cantidad        NVARCHAR(50)  '$.cantidad',
            unidad          NVARCHAR(50)  '$.unidad',
            marca           NVARCHAR(200) '$.marca',
            mano_obra       NVARCHAR(50)  '$.mano_obra',
            precio_unitario NVARCHAR(50)  '$.precio_unitario',
            total           NVARCHAR(50)  '$.total',
            moneda          NVARCHAR(10)  '$.moneda',
            tiempo_entrega  NVARCHAR(200) '$.tiempo_entrega'
        ) AS j;

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

PRINT 'sp_ActualizarCotizacion creado correctamente.';
GO
