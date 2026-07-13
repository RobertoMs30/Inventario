-- ============================================================
--  MÓDULO COTIZACIONES — TU EMPRESA
--  Ejecutar en la base de datos: TU_BASE_DE_DATOS
-- ============================================================

-- ============================================================
-- 1. TABLA ENCABEZADO: inventario.cotizaciones
-- ============================================================
IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'inventario' AND t.name = 'cotizaciones'
)
BEGIN
    CREATE TABLE inventario.cotizaciones (
        id          INT IDENTITY(1,1) NOT NULL,
        folio       NVARCHAR(50)      NOT NULL,   -- Generado automáticamente: COT-AAAA-NNNN
        fecha       DATE              NOT NULL,
        cliente     NVARCHAR(200)     NOT NULL,
        proyecto    NVARCHAR(300)     NOT NULL,
        fecha_reg   DATETIME          NOT NULL CONSTRAINT DF_cotizaciones_fecha_reg DEFAULT (GETDATE()),
        CONSTRAINT PK_cotizaciones PRIMARY KEY (id)
    );
    PRINT 'Tabla inventario.cotizaciones creada correctamente.';
END
ELSE
    PRINT 'Tabla inventario.cotizaciones ya existe. Sin cambios.';
GO

-- ============================================================
-- 2. TABLA ÍTEMS: inventario.cotizacion_items
-- ============================================================
IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'inventario' AND t.name = 'cotizacion_items'
)
BEGIN
    CREATE TABLE inventario.cotizacion_items (
        id               INT IDENTITY(1,1) NOT NULL,
        cotizacion_id    INT               NOT NULL,
        no_item          INT               NOT NULL,   -- 10, 20, 30 ...
        descripcion      NVARCHAR(500)     NOT NULL,
        cantidad         DECIMAL(18,2)     NOT NULL,
        unidad           NVARCHAR(50)      NOT NULL,
        marca            NVARCHAR(200)     NULL,
        mano_obra        DECIMAL(18,2)     NOT NULL CONSTRAINT DF_cot_items_mano_obra DEFAULT (0),
        precio_unitario  DECIMAL(18,2)     NOT NULL CONSTRAINT DF_cot_items_pu DEFAULT (0),
        total            DECIMAL(18,2)     NOT NULL,   -- (precio_unitario + mano_obra) * cantidad
        moneda           NVARCHAR(10)      NOT NULL,   -- MXN | USD
        tiempo_entrega   NVARCHAR(200)     NULL,
        CONSTRAINT PK_cotizacion_items    PRIMARY KEY (id),
        CONSTRAINT FK_items_cotizacion    FOREIGN KEY (cotizacion_id)
            REFERENCES inventario.cotizaciones(id)
    );
    PRINT 'Tabla inventario.cotizacion_items creada correctamente.';
END
ELSE
    PRINT 'Tabla inventario.cotizacion_items ya existe. Sin cambios.';
GO

-- ============================================================
-- 3. SP: sp_CrearCotizacion
--    Inserta encabezado + ítems (pasados como JSON)
--    Genera folio automático: COT-{YEAR}-{ID:0000}
-- ============================================================
IF OBJECT_ID('dbo.sp_CrearCotizacion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CrearCotizacion;
GO

CREATE PROCEDURE dbo.sp_CrearCotizacion
    @fecha    DATE,
    @cliente  NVARCHAR(200),
    @proyecto NVARCHAR(300),
    @items    NVARCHAR(MAX)   -- JSON: [{no_item,descripcion,cantidad,unidad,marca,mano_obra,precio_unitario,total,moneda,tiempo_entrega}]
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- 1) Insertar encabezado con folio temporal
        INSERT INTO inventario.cotizaciones (folio, fecha, cliente, proyecto)
        VALUES ('TEMP', @fecha, @cliente, @proyecto);

        DECLARE @newId INT = SCOPE_IDENTITY();

        -- 2) Actualizar folio con el ID real: COT-AAAA-NNNN
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

        -- Retornar el ID de la cotización creada
        SELECT @newId AS id;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

PRINT 'Stored procedure sp_CrearCotizacion creado correctamente.';
GO

-- ============================================================
-- 4. SP: sp_ObtenerCotizaciones
--    Lista paginada de cotizaciones con búsqueda opcional
-- ============================================================
IF OBJECT_ID('dbo.sp_ObtenerCotizaciones', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ObtenerCotizaciones;
GO

CREATE PROCEDURE dbo.sp_ObtenerCotizaciones
    @q        NVARCHAR(300) = NULL,
    @offset   INT           = 0,
    @pageSize INT           = 25
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @filtro NVARCHAR(310) = CASE WHEN @q IS NOT NULL THEN '%' + @q + '%' ELSE NULL END;

    -- Total
    SELECT COUNT(*) AS total
    FROM inventario.cotizaciones
    WHERE @filtro IS NULL
       OR folio    LIKE @filtro
       OR cliente  LIKE @filtro
       OR proyecto LIKE @filtro;

    -- Página
    SELECT id, folio, fecha, cliente, proyecto, fecha_reg
    FROM inventario.cotizaciones
    WHERE @filtro IS NULL
       OR folio    LIKE @filtro
       OR cliente  LIKE @filtro
       OR proyecto LIKE @filtro
    ORDER BY id DESC
    OFFSET @offset ROWS
    FETCH NEXT @pageSize ROWS ONLY;
END;
GO

PRINT 'Stored procedure sp_ObtenerCotizaciones creado correctamente.';
GO

-- ============================================================
-- 5. SP: sp_ObtenerCotizacionDetalle
--    Retorna encabezado + ítems de una cotización por ID
-- ============================================================
IF OBJECT_ID('dbo.sp_ObtenerCotizacionDetalle', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ObtenerCotizacionDetalle;
GO

CREATE PROCEDURE dbo.sp_ObtenerCotizacionDetalle
    @id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Encabezado
    SELECT id, folio, fecha, cliente, proyecto, fecha_reg
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

PRINT 'Stored procedure sp_ObtenerCotizacionDetalle creado correctamente.';
GO

PRINT '';
PRINT '====================================================';
PRINT ' Módulo de Cotizaciones instalado correctamente.';
PRINT '====================================================';
