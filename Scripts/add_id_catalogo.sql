-- Agregar columna id IDENTITY a catalogo_materiales si no existe
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.catalogo_materiales')
      AND name = 'id'
)
BEGIN
    ALTER TABLE inventario.catalogo_materiales
        ADD id INT IDENTITY(1,1);
    PRINT 'Columna id agregada correctamente.';
END
ELSE
BEGIN
    PRINT 'La columna id ya existe.';
END
