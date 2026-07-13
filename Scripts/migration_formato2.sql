-- ============================================================
--  MIGRACIÓN: Formato 2 — Cotizaciones con costos internos
--  Ejecutar en: TU_BASE_DE_DATOS
-- ============================================================

-- ── 1) Nuevas columnas en inventario.cotizaciones ──────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'formato')
BEGIN
    ALTER TABLE inventario.cotizaciones ADD formato TINYINT NOT NULL CONSTRAINT DF_cotizaciones_formato DEFAULT (1);
    PRINT 'Columna formato agregada.';
END
ELSE
    PRINT 'Columna formato ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'solicitante')
BEGIN
    ALTER TABLE inventario.cotizaciones ADD solicitante NVARCHAR(200) NULL;
    PRINT 'Columna solicitante agregada.';
END
ELSE
    PRINT 'Columna solicitante ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'localidad')
BEGIN
    ALTER TABLE inventario.cotizaciones ADD localidad NVARCHAR(200) NULL;
    PRINT 'Columna localidad agregada.';
END
ELSE
    PRINT 'Columna localidad ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'no_puertos')
BEGIN
    ALTER TABLE inventario.cotizaciones ADD no_puertos NVARCHAR(50) NULL;
    PRINT 'Columna no_puertos agregada.';
END
ELSE
    PRINT 'Columna no_puertos ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizaciones') AND name = 'seccion_titulo')
BEGIN
    ALTER TABLE inventario.cotizaciones ADD seccion_titulo NVARCHAR(300) NULL;
    PRINT 'Columna seccion_titulo agregada.';
END
ELSE
    PRINT 'Columna seccion_titulo ya existe.';
GO

-- ── 2) Nuevas columnas en inventario.cotizacion_items ──────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizacion_items') AND name = 'costo_usd')
BEGIN
    ALTER TABLE inventario.cotizacion_items ADD costo_usd DECIMAL(18,4) NULL;
    PRINT 'Columna costo_usd agregada.';
END
ELSE
    PRINT 'Columna costo_usd ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizacion_items') AND name = 'tc')
BEGIN
    ALTER TABLE inventario.cotizacion_items ADD tc DECIMAL(10,4) NULL;
    PRINT 'Columna tc agregada.';
END
ELSE
    PRINT 'Columna tc ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizacion_items') AND name = 'pct_venta')
BEGIN
    ALTER TABLE inventario.cotizacion_items ADD pct_venta DECIMAL(10,4) NULL;
    PRINT 'Columna pct_venta agregada.';
END
ELSE
    PRINT 'Columna pct_venta ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('inventario.cotizacion_items') AND name = 'pct_mo')
BEGIN
    ALTER TABLE inventario.cotizacion_items ADD pct_mo DECIMAL(10,4) NULL;
    PRINT 'Columna pct_mo agregada.';
END
ELSE
    PRINT 'Columna pct_mo ya existe.';
GO

-- ── 3) Actualizar sp_CrearCotizacion ────────────────────────

IF OBJECT_ID('dbo.sp_CrearCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CrearCotizacion;
GO

CREATE PROCEDURE dbo.sp_CrearCotizacion
    @fecha          DATE,
    @cliente        NVARCHAR(200),
    @proyecto       NVARCHAR(300),
    @items          NVARCHAR(MAX),
    @rfq            NVARCHAR(100)  = NULL,
    @descuento_pct  DECIMAL(5,2)   = 0,
    @formato        TINYINT        = 1,
    @solicitante    NVARCHAR(200)  = NULL,
    @localidad      NVARCHAR(200)  = NULL,
    @no_puertos     NVARCHAR(50)   = NULL,
    @seccion_titulo NVARCHAR(300)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY

        INSERT INTO inventario.cotizaciones
            (folio, fecha, cliente, proyecto, rfq, descuento_pct,
             formato, solicitante, localidad, no_puertos, seccion_titulo)
        VALUES
            ('TEMP', @fecha, @cliente, @proyecto, @rfq, ISNULL(@descuento_pct, 0),
             ISNULL(@formato, 1), @solicitante, @localidad, @no_puertos, @seccion_titulo);

        DECLARE @newId INT = SCOPE_IDENTITY();

        UPDATE inventario.cotizaciones
        SET folio = CONCAT('COT-', YEAR(@fecha), '-', RIGHT('0000' + CAST(@newId AS NVARCHAR(10)), 4))
        WHERE id = @newId;

        INSERT INTO inventario.cotizacion_items
            (cotizacion_id, no_item, descripcion, cantidad, unidad, marca,
             mano_obra, precio_unitario, total, moneda, tiempo_entrega,
             costo_usd, tc, pct_venta, pct_mo)
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
            j.tiempo_entrega,
            CASE WHEN j.costo_usd IS NOT NULL THEN CAST(j.costo_usd AS DECIMAL(18,4)) ELSE NULL END,
            CASE WHEN j.tc        IS NOT NULL THEN CAST(j.tc        AS DECIMAL(10,4)) ELSE NULL END,
            CASE WHEN j.pct_venta IS NOT NULL THEN CAST(j.pct_venta AS DECIMAL(10,4)) ELSE NULL END,
            CASE WHEN j.pct_mo    IS NOT NULL THEN CAST(j.pct_mo    AS DECIMAL(10,4)) ELSE NULL END
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
            tiempo_entrega  NVARCHAR(200) '$.tiempo_entrega',
            costo_usd       NVARCHAR(50)  '$.costo_usd',
            tc              NVARCHAR(50)  '$.tc',
            pct_venta       NVARCHAR(50)  '$.pct_venta',
            pct_mo          NVARCHAR(50)  '$.pct_mo'
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

-- ── 4) Actualizar sp_ActualizarCotizacion ───────────────────

IF OBJECT_ID('dbo.sp_ActualizarCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ActualizarCotizacion;
GO

CREATE PROCEDURE dbo.sp_ActualizarCotizacion
    @id             INT,
    @fecha          DATE,
    @cliente        NVARCHAR(200),
    @proyecto       NVARCHAR(300),
    @items          NVARCHAR(MAX),
    @rfq            NVARCHAR(100)  = NULL,
    @descuento_pct  DECIMAL(5,2)   = 0,
    @formato        TINYINT        = 1,
    @solicitante    NVARCHAR(200)  = NULL,
    @localidad      NVARCHAR(200)  = NULL,
    @no_puertos     NVARCHAR(50)   = NULL,
    @seccion_titulo NVARCHAR(300)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY

        IF NOT EXISTS (SELECT 1 FROM inventario.cotizaciones WHERE id = @id)
            THROW 50001, 'Cotización no encontrada.', 1;

        UPDATE inventario.cotizaciones
        SET fecha          = @fecha,
            cliente        = @cliente,
            proyecto       = @proyecto,
            rfq            = @rfq,
            descuento_pct  = ISNULL(@descuento_pct, 0),
            formato        = ISNULL(@formato, 1),
            solicitante    = @solicitante,
            localidad      = @localidad,
            no_puertos     = @no_puertos,
            seccion_titulo = @seccion_titulo
        WHERE id = @id;

        DELETE FROM inventario.cotizacion_items WHERE cotizacion_id = @id;

        INSERT INTO inventario.cotizacion_items
            (cotizacion_id, no_item, descripcion, cantidad, unidad, marca,
             mano_obra, precio_unitario, total, moneda, tiempo_entrega,
             costo_usd, tc, pct_venta, pct_mo)
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
            j.tiempo_entrega,
            CASE WHEN j.costo_usd IS NOT NULL THEN CAST(j.costo_usd AS DECIMAL(18,4)) ELSE NULL END,
            CASE WHEN j.tc        IS NOT NULL THEN CAST(j.tc        AS DECIMAL(10,4)) ELSE NULL END,
            CASE WHEN j.pct_venta IS NOT NULL THEN CAST(j.pct_venta AS DECIMAL(10,4)) ELSE NULL END,
            CASE WHEN j.pct_mo    IS NOT NULL THEN CAST(j.pct_mo    AS DECIMAL(10,4)) ELSE NULL END
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
            tiempo_entrega  NVARCHAR(200) '$.tiempo_entrega',
            costo_usd       NVARCHAR(50)  '$.costo_usd',
            tc              NVARCHAR(50)  '$.tc',
            pct_venta       NVARCHAR(50)  '$.pct_venta',
            pct_mo          NVARCHAR(50)  '$.pct_mo'
        ) AS j;

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
PRINT 'sp_ActualizarCotizacion actualizado.';
GO

-- ── 5) Actualizar sp_ObtenerCotizacionDetalle ───────────────

IF OBJECT_ID('dbo.sp_ObtenerCotizacionDetalle', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ObtenerCotizacionDetalle;
GO

CREATE PROCEDURE dbo.sp_ObtenerCotizacionDetalle
    @id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT id, folio, fecha, cliente, proyecto, fecha_reg,
           ISNULL(rfq, '')            AS rfq,
           ISNULL(descuento_pct, 0)   AS descuento_pct,
           ISNULL(formato, 1)         AS formato,
           ISNULL(solicitante, '')    AS solicitante,
           ISNULL(localidad, '')      AS localidad,
           ISNULL(no_puertos, '')     AS no_puertos,
           ISNULL(seccion_titulo, '') AS seccion_titulo
    FROM inventario.cotizaciones
    WHERE id = @id;

    SELECT id, no_item, descripcion, cantidad, unidad, marca,
           mano_obra, precio_unitario, total, moneda, tiempo_entrega,
           costo_usd, tc, pct_venta, pct_mo
    FROM inventario.cotizacion_items
    WHERE cotizacion_id = @id
    ORDER BY no_item;
END;
GO
PRINT 'sp_ObtenerCotizacionDetalle actualizado.';
GO

PRINT '';
PRINT '====================================================';
PRINT ' Migración Formato 2 completada correctamente.';
PRINT '====================================================';
