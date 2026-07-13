-- ============================================================
--  MIGRACIÓN: Agregar campos RFQ y Descuento a cotizaciones
--  Ejecutar en: TU_BASE_DE_DATOS
-- ============================================================

-- 1) Nuevas columnas en inventario.cotizaciones
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'rfq'
)
BEGIN
    ALTER TABLE inventario.cotizaciones ADD rfq NVARCHAR(100) NULL;
    PRINT 'Columna rfq agregada.';
END
ELSE
    PRINT 'Columna rfq ya existe.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'descuento_pct'
)
BEGIN
    ALTER TABLE inventario.cotizaciones ADD descuento_pct DECIMAL(5,2) NOT NULL CONSTRAINT DF_cotizaciones_descuento DEFAULT (0);
    PRINT 'Columna descuento_pct agregada.';
END
ELSE
    PRINT 'Columna descuento_pct ya existe.';
GO

-- ============================================================
-- 2) Actualizar sp_CrearCotizacion (agrega @rfq y @descuento_pct)
-- ============================================================
IF OBJECT_ID('dbo.sp_CrearCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CrearCotizacion;
GO

CREATE PROCEDURE dbo.sp_CrearCotizacion
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

        -- 1) Insertar encabezado con folio temporal
        INSERT INTO inventario.cotizaciones (folio, fecha, cliente, proyecto, rfq, descuento_pct)
        VALUES ('TEMP', @fecha, @cliente, @proyecto, @rfq, ISNULL(@descuento_pct, 0));

        DECLARE @newId INT = SCOPE_IDENTITY();

        -- 2) Actualizar folio: COT-AAAA-NNNN
        UPDATE inventario.cotizaciones
        SET folio = CONCAT('COT-', YEAR(@fecha), '-', RIGHT('0000' + CAST(@newId AS NVARCHAR(10)), 4))
        WHERE id = @newId;

        -- 3) Insertar ítems desde JSON
        INSERT INTO inventario.cotizacion_items
            (cotizacion_id, no_item, descripcion, cantidad, unidad, marca,
             mano_obra, precio_unitario, total, moneda, tiempo_entrega)
        SELECT
            @newId,
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
        SELECT @newId AS id;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

PRINT 'sp_CrearCotizacion actualizado.';
GO

-- ============================================================
-- 3) Actualizar sp_ObtenerCotizacionDetalle (retorna rfq y descuento_pct)
-- ============================================================
IF OBJECT_ID('dbo.sp_ObtenerCotizacionDetalle', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ObtenerCotizacionDetalle;
GO

CREATE PROCEDURE dbo.sp_ObtenerCotizacionDetalle
    @id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Encabezado (incluye rfq y descuento_pct)
    SELECT id, folio, fecha, cliente, proyecto, fecha_reg,
           ISNULL(rfq, '') AS rfq,
           ISNULL(descuento_pct, 0) AS descuento_pct
    FROM inventario.cotizaciones
    WHERE id = @id;

    -- Ítems ordenados
    SELECT id, no_item, descripcion, cantidad, unidad, marca,
           mano_obra, precio_unitario, total, moneda, tiempo_entrega
    FROM inventario.cotizacion_items
    WHERE cotizacion_id = @id
    ORDER BY no_item;
END;
GO

PRINT 'sp_ObtenerCotizacionDetalle actualizado.';
GO

PRINT '';
PRINT '====================================================';
PRINT ' Migración completada correctamente.';
PRINT '====================================================';
