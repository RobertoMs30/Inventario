-- ============================================================
--  CONSECUTIVO PROYECTOS — TU EMPRESA
--  Ejecutar UNA SOLA VEZ en la base de datos: TU_BASE_DE_DATOS
--  Conecta inventario.cotizaciones con administracion_proyectos.proyectos
-- ============================================================

-- ============================================================
-- 1. Agregar columnas a inventario.cotizaciones
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.cotizaciones')
      AND name = 'consecutivo'
)
BEGIN
    ALTER TABLE inventario.cotizaciones
        ADD consecutivo INT NULL;
    PRINT 'Columna consecutivo agregada a inventario.cotizaciones.';
END
ELSE
    PRINT 'Columna consecutivo ya existe. Sin cambios.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.cotizaciones')
      AND name = 'id_proyecto'
)
BEGIN
    ALTER TABLE inventario.cotizaciones
        ADD id_proyecto INT NULL;
    PRINT 'Columna id_proyecto agregada a inventario.cotizaciones.';
END
ELSE
    PRINT 'Columna id_proyecto ya existe. Sin cambios.';
GO

-- ============================================================
-- 2. Recrear sp_CrearCotizacion con lógica de consecutivo
-- ============================================================
IF OBJECT_ID('dbo.sp_CrearCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CrearCotizacion;
GO

CREATE PROCEDURE dbo.sp_CrearCotizacion
    @fecha                  DATE,
    @cliente                NVARCHAR(200),
    @proyecto               NVARCHAR(300),
    @items                  NVARCHAR(MAX),
    @rfq                    NVARCHAR(100)  = NULL,
    @descuento_pct          DECIMAL(5,2)   = 0,
    @formato                TINYINT        = 1,
    @solicitante            NVARCHAR(200)  = NULL,
    @responsable            NVARCHAR(200)  = NULL,
    @localidad              NVARCHAR(200)  = NULL,
    @no_puertos             NVARCHAR(50)   = NULL,
    @seccion_titulo         NVARCHAR(300)  = NULL,
    @condiciones_comerciales NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Usar nivel de aislamiento SERIALIZABLE para esta transacción
    -- evita que dos usuarios simultáneos lean el mismo MAX consecutivo
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- ── 1) Obtener el siguiente consecutivo de forma segura ──────
        DECLARE @sig INT;
        SELECT @sig = ISNULL(MAX(TRY_CAST(cotizacion AS INT)), 0) + 1
        FROM administracion_proyectos.proyectos WITH (UPDLOCK, HOLDLOCK);

        -- ── 2) Insertar fila en administracion_proyectos.proyectos ───
        --       Solo llenamos los campos que tenemos; el resto queda NULL
        INSERT INTO administracion_proyectos.proyectos
            (cotizacion, numcotizacion, proyecto)
        VALUES
            (CAST(@sig AS NVARCHAR(50)), @sig, @proyecto);

        DECLARE @id_proy INT = SCOPE_IDENTITY();

        -- ── 3) Insertar encabezado de cotización con folio temporal ──
        INSERT INTO inventario.cotizaciones
            (folio, fecha, cliente, proyecto,
             rfq, descuento_pct, formato,
             solicitante, responsable, localidad, no_puertos,
             seccion_titulo, condiciones_comerciales,
             consecutivo, id_proyecto)
        VALUES
            ('TEMP', @fecha, @cliente, @proyecto,
             @rfq, @descuento_pct, @formato,
             @solicitante, @responsable, @localidad, @no_puertos,
             @seccion_titulo, @condiciones_comerciales,
             @sig, @id_proy);

        DECLARE @newId INT = SCOPE_IDENTITY();

        -- ── 4) Actualizar folio con el ID real: COT-AAAA-NNNN ───────
        UPDATE inventario.cotizaciones
        SET folio = CONCAT('COT-', YEAR(@fecha), '-',
                           RIGHT('0000' + CAST(@newId AS NVARCHAR(10)), 4))
        WHERE id = @newId;

        -- ── 5) Insertar ítems desde JSON ─────────────────────────────
        INSERT INTO inventario.cotizacion_items
            (cotizacion_id, no_item, descripcion, cantidad, unidad, marca,
             mano_obra, precio_unitario, total, moneda, tiempo_entrega)
        SELECT
            @newId,
            CAST(j.no_item          AS INT),
            j.descripcion,
            CAST(j.cantidad         AS DECIMAL(18,4)),
            j.unidad,
            j.marca,
            CAST(j.mano_obra        AS DECIMAL(18,4)),
            CAST(j.precio_unitario  AS DECIMAL(18,4)),
            CAST(j.total            AS DECIMAL(18,4)),
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

        -- Retornar el ID de la cotización creada
        SELECT @newId AS id;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

PRINT 'sp_CrearCotizacion recreado con lógica de consecutivo.';
GO

-- ============================================================
-- 3. Recrear sp_ObtenerCotizacionDetalle devolviendo consecutivo
-- ============================================================
IF OBJECT_ID('dbo.sp_ObtenerCotizacionDetalle', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ObtenerCotizacionDetalle;
GO

CREATE PROCEDURE dbo.sp_ObtenerCotizacionDetalle
    @id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Encabezado — ahora incluye consecutivo e id_proyecto
    SELECT id, folio, fecha, cliente, proyecto, fecha_reg,
           rfq, descuento_pct, solicitante, responsable,
           consecutivo, id_proyecto
    FROM inventario.cotizaciones
    WHERE id = @id;

    -- Ítems ordenados (sin cambios)
    SELECT id, no_item, descripcion, cantidad, unidad, marca,
           mano_obra, precio_unitario, total, moneda, tiempo_entrega
    FROM inventario.cotizacion_items
    WHERE cotizacion_id = @id
    ORDER BY no_item;
END;
GO

PRINT 'sp_ObtenerCotizacionDetalle recreado con campo consecutivo.';
GO

PRINT '';
PRINT '====================================================';
PRINT ' Consecutivo de proyectos instalado correctamente.';
PRINT ' Recuerda ejecutar este script UNA SOLA VEZ.';
PRINT '====================================================';
