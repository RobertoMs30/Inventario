-- Agregar columna fecha_registro a entradas_inventario
-- Captura automaticamente la fecha real en que se registro la entrada
-- (distinta de fecha_compra, que la captura el usuario).
-- Los registros existentes quedan en NULL; solo aplica de aqui en adelante.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.entradas_inventario')
      AND name = 'fecha_registro'
)
BEGIN
    -- Columna NULLABLE + DEFAULT sin WITH VALUES:
    -- las filas existentes quedan en NULL y cada insercion nueva
    -- se rellena automaticamente con la fecha de hoy.
    ALTER TABLE inventario.entradas_inventario
        ADD fecha_registro DATE NULL
            CONSTRAINT DF_entradas_inventario_fecha_registro DEFAULT (CONVERT(date, GETDATE()));
    PRINT 'Columna fecha_registro agregada. Antiguos en NULL, nuevos con fecha de hoy.';
END
ELSE
BEGIN
    PRINT 'La columna fecha_registro ya existe.';
END
GO
