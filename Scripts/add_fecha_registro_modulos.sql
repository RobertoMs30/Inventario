-- ============================================================
--  Agregar columna fecha_registro a varios modulos
--  Captura automaticamente la fecha real en que se registro la fila.
--  Registros existentes quedan en NULL; aplica solo de aqui en adelante.
--  Ejecutar en la base de datos: TU_BASE_DE_DATOS
--  NOTA: inventario.cotizaciones NO se incluye porque ya tiene
--        su propia columna de registro: fecha_reg (DATETIME DEFAULT GETDATE()).
-- ============================================================

-- ── 1. SALIDAS DE MATERIAL ──────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.salidas_inventario')
      AND name = 'fecha_registro'
)
BEGIN
    ALTER TABLE inventario.salidas_inventario
        ADD fecha_registro DATE NULL
            CONSTRAINT DF_salidas_inventario_fecha_registro DEFAULT (CONVERT(date, GETDATE()));
    PRINT 'salidas_inventario: columna fecha_registro agregada.';
END
ELSE
    PRINT 'salidas_inventario: la columna fecha_registro ya existe.';
GO

-- ── 2. DEVOLUCIONES ─────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.devoluciones_inventario')
      AND name = 'fecha_registro'
)
BEGIN
    ALTER TABLE inventario.devoluciones_inventario
        ADD fecha_registro DATE NULL
            CONSTRAINT DF_devoluciones_inventario_fecha_registro DEFAULT (CONVERT(date, GETDATE()));
    PRINT 'devoluciones_inventario: columna fecha_registro agregada.';
END
ELSE
    PRINT 'devoluciones_inventario: la columna fecha_registro ya existe.';
GO

-- ── 3. ORDENES DE COMPRA ────────────────────────────────────
-- OJO: esta tabla vive en el esquema administracion_proyectos, NO en inventario.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('administracion_proyectos.ordenes_compra')
      AND name = 'fecha_registro'
)
BEGIN
    ALTER TABLE administracion_proyectos.ordenes_compra
        ADD fecha_registro DATE NULL
            CONSTRAINT DF_ordenes_compra_fecha_registro DEFAULT (CONVERT(date, GETDATE()));
    PRINT 'ordenes_compra: columna fecha_registro agregada.';
END
ELSE
    PRINT 'ordenes_compra: la columna fecha_registro ya existe.';
GO
