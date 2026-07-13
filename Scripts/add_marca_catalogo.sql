-- Agregar columna marca a catalogo_materiales
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inventario.catalogo_materiales')
      AND name = 'marca'
)
BEGIN
    ALTER TABLE inventario.catalogo_materiales ADD marca NVARCHAR(100) NULL;
    PRINT 'Columna marca agregada.';
END
ELSE
BEGIN
    PRINT 'La columna marca ya existe.';
END
