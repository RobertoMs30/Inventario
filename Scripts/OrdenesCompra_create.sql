-- ============================================================
--  MÓDULO ORDENES DE COMPRA — TU EMPRESA
--  Ejecutar en la base de datos: TU_BASE_DE_DATOS
--  Fecha: 2026-03-31
-- ============================================================

-- ============================================================
-- 1. TABLA: inventario.ordenes_compra
-- ============================================================
IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'inventario' AND t.name = 'ordenes_compra'
)
BEGIN
    CREATE TABLE inventario.ordenes_compra (
        id          INT           NOT NULL IDENTITY(1,1),
        oc          NVARCHAR(100) NULL,          -- Número de orden de compra
        proveedor   NVARCHAR(200) NULL,          -- Nombre del proveedor
        elaboro     NVARCHAR(200) NULL,          -- Quién elaboró la OC
        fecha       DATE          NULL,          -- Fecha de la orden
        proyecto    NVARCHAR(200) NULL,          -- Número de proyecto
        cotizacion  NVARCHAR(200) NULL,          -- Folio de cotización relacionada (lógico, sin FK formal)
        CONSTRAINT PK_ordenes_compra PRIMARY KEY (id)
    );
    PRINT 'Tabla inventario.ordenes_compra creada correctamente.';
END
ELSE
    PRINT 'Tabla inventario.ordenes_compra ya existe. Sin cambios.';
GO

-- ============================================================
-- 2. SP: sp_RegistrarOrdenCompra
-- ============================================================
IF OBJECT_ID('dbo.sp_RegistrarOrdenCompra', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_RegistrarOrdenCompra;
GO

CREATE PROCEDURE dbo.sp_RegistrarOrdenCompra
    @oc         NVARCHAR(100) = NULL,
    @proveedor  NVARCHAR(200) = NULL,
    @elaboro    NVARCHAR(200) = NULL,
    @fecha      DATE          = NULL,
    @proyecto   NVARCHAR(200) = NULL,
    @cotizacion NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO inventario.ordenes_compra (oc, proveedor, elaboro, fecha, proyecto, cotizacion)
    VALUES (
        NULLIF(LTRIM(RTRIM(@oc)),        ''),
        NULLIF(LTRIM(RTRIM(@proveedor)), ''),
        NULLIF(LTRIM(RTRIM(@elaboro)),   ''),
        @fecha,
        NULLIF(LTRIM(RTRIM(@proyecto)),  ''),
        NULLIF(LTRIM(RTRIM(@cotizacion)),'')
    );

    -- Retornar el ID generado
    SELECT SCOPE_IDENTITY() AS id;
END;
GO
PRINT 'SP sp_RegistrarOrdenCompra creado correctamente.';
GO

-- ============================================================
-- 3. SP: sp_ActualizarOrdenCompra
-- ============================================================
IF OBJECT_ID('dbo.sp_ActualizarOrdenCompra', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ActualizarOrdenCompra;
GO

CREATE PROCEDURE dbo.sp_ActualizarOrdenCompra
    @id         INT,
    @oc         NVARCHAR(100) = NULL,
    @proveedor  NVARCHAR(200) = NULL,
    @elaboro    NVARCHAR(200) = NULL,
    @fecha      DATE          = NULL,
    @proyecto   NVARCHAR(200) = NULL,
    @cotizacion NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inventario.ordenes_compra WHERE id = @id)
        THROW 50001, 'Orden de compra no encontrada.', 1;

    UPDATE inventario.ordenes_compra
    SET oc         = NULLIF(LTRIM(RTRIM(@oc)),        ''),
        proveedor  = NULLIF(LTRIM(RTRIM(@proveedor)), ''),
        elaboro    = NULLIF(LTRIM(RTRIM(@elaboro)),   ''),
        fecha      = @fecha,
        proyecto   = NULLIF(LTRIM(RTRIM(@proyecto)),  ''),
        cotizacion = NULLIF(LTRIM(RTRIM(@cotizacion)),'')
    WHERE id = @id;
END;
GO
PRINT 'SP sp_ActualizarOrdenCompra creado correctamente.';
GO

-- ============================================================
-- NOTA: Importar historial desde Excel
-- ============================================================
-- El historial existente (~6,000 registros) se importa mediante:
-- SSMS → clic derecho en la BD → Tasks → Import Data
-- Origen: Microsoft Excel (.xlsx)
-- Destino: inventario.ordenes_compra
-- Mapear columnas: OC → oc, Proveedor → proveedor,
--   Elaboró → elaboro, Fecha → fecha,
--   Proyecto → proyecto, Cotización → cotizacion
-- ============================================================

PRINT '';
PRINT '====================================================';
PRINT ' Módulo Ordenes de Compra instalado correctamente.';
PRINT '====================================================';
