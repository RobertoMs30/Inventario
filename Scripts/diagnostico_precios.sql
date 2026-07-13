-- DIAGNOSTICO 1: cuantos materiales sin precio hay en el catalogo
SELECT COUNT(*) AS sin_precio
FROM inventario.catalogo_materiales
WHERE pu IS NULL OR pu = 0;

-- DIAGNOSTICO 2: primeros 50 materiales sin precio (para comparar descripciones)
SELECT TOP 50 id, cod_fab, descripcion, pu, moneda
FROM inventario.catalogo_materiales
WHERE pu IS NULL OR pu = 0
ORDER BY descripcion;

-- DIAGNOSTICO 3: prueba manual con LIKE
SELECT id, descripcion, pu, moneda
FROM inventario.catalogo_materiales
WHERE UPPER(LTRIM(RTRIM(descripcion))) LIKE '%COPLE%';
