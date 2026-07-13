-- ============================================================
--  DIAGNÓSTICO DE INTEGRIDAD — TU_BASE_DE_DATOS
--  SOLO LECTURA. No modifica nada. Seguro de ejecutar.
--  Objetivo: medir el "daño" antes de poner llaves, índices y FKs.
--  Ejecutar en: TU_BASE_DE_DATOS  (modo "Results to Grid")
-- ============================================================
USE TU_BASE_DE_DATOS;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 1 — Estructura actual del catálogo';
PRINT '===========================================================';

-- ¿Tiene PK? ¿Tiene índices? ¿cod_fab es único?
SELECT
    i.name              AS indice,
    i.type_desc         AS tipo,
    i.is_primary_key    AS es_pk,
    i.is_unique         AS es_unico,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columnas
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c        ON c.object_id  = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID('inventario.catalogo_materiales')
GROUP BY i.name, i.type_desc, i.is_primary_key, i.is_unique;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 2 — cod_fab duplicados en catálogo';
PRINT ' (bloquean crear una UNIQUE / PK natural)';
PRINT '===========================================================';

-- Duplicados comparando ya SIN espacios (como hace la app)
SELECT
    LTRIM(RTRIM(cod_fab)) AS cod_fab_limpio,
    COUNT(*)              AS veces
FROM inventario.catalogo_materiales
GROUP BY LTRIM(RTRIM(cod_fab))
HAVING COUNT(*) > 1
ORDER BY veces DESC;

-- Total de filas afectadas por duplicados
SELECT SUM(veces) AS filas_en_grupos_duplicados, COUNT(*) AS grupos_duplicados
FROM (
    SELECT COUNT(*) AS veces
    FROM inventario.catalogo_materiales
    GROUP BY LTRIM(RTRIM(cod_fab))
    HAVING COUNT(*) > 1
) t;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 3 — cod_fab con espacios / NULL / vacío';
PRINT ' (la suciedad que obliga a LTRIM(RTRIM) en cada query)';
PRINT '===========================================================';

SELECT
    SUM(CASE WHEN cod_fab IS NULL                              THEN 1 ELSE 0 END) AS nulos,
    SUM(CASE WHEN cod_fab IS NOT NULL AND LTRIM(RTRIM(cod_fab)) = '' THEN 1 ELSE 0 END) AS vacios,
    SUM(CASE WHEN cod_fab <> LTRIM(RTRIM(cod_fab))             THEN 1 ELSE 0 END) AS con_espacios
FROM inventario.catalogo_materiales;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 4 — Movimientos HUÉRFANOS';
PRINT ' (entradas/salidas/devoluciones cuyo cod_fab no existe';
PRINT '  en el catálogo → estos bloquean una FK)';
PRINT '===========================================================';

-- Entradas huérfanas
SELECT 'entradas_inventario' AS tabla, COUNT(*) AS huerfanos
FROM inventario.entradas_inventario e
WHERE NOT EXISTS (
    SELECT 1 FROM inventario.catalogo_materiales c
    WHERE LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(e.cod_fab))
)
UNION ALL
-- Salidas huérfanas
SELECT 'salidas_inventario', COUNT(*)
FROM inventario.salidas_inventario s
WHERE NOT EXISTS (
    SELECT 1 FROM inventario.catalogo_materiales c
    WHERE LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(s.cod_fab))
)
UNION ALL
-- Devoluciones huérfanas
SELECT 'devoluciones_inventario', COUNT(*)
FROM inventario.devoluciones_inventario d
WHERE NOT EXISTS (
    SELECT 1 FROM inventario.catalogo_materiales c
    WHERE LTRIM(RTRIM(c.cod_fab)) = LTRIM(RTRIM(d.cod_fab))
);
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 5 — Stock: ¿cant coincide con los movimientos?';
PRINT ' Esperado(cant) = SUM(entradas) - SUM(salidas) + SUM(devoluciones)';
PRINT ' Muestra los 50 materiales con mayor descuadre';
PRINT '===========================================================';

WITH ent AS (
    SELECT LTRIM(RTRIM(cod_fab)) AS cf, SUM(cantidad) AS q
    FROM inventario.entradas_inventario GROUP BY LTRIM(RTRIM(cod_fab))
),
sal AS (
    SELECT LTRIM(RTRIM(cod_fab)) AS cf, SUM(cantidad) AS q
    FROM inventario.salidas_inventario GROUP BY LTRIM(RTRIM(cod_fab))
),
dev AS (
    SELECT LTRIM(RTRIM(cod_fab)) AS cf, SUM(cantidad) AS q
    FROM inventario.devoluciones_inventario GROUP BY LTRIM(RTRIM(cod_fab))
)
SELECT TOP 50
    LTRIM(RTRIM(c.cod_fab))                                          AS cod_fab,
    c.cant                                                          AS cant_actual,
    ISNULL(ent.q,0) - ISNULL(sal.q,0) + ISNULL(dev.q,0)            AS cant_esperada,
    c.cant - (ISNULL(ent.q,0) - ISNULL(sal.q,0) + ISNULL(dev.q,0)) AS descuadre
FROM inventario.catalogo_materiales c
LEFT JOIN ent ON ent.cf = LTRIM(RTRIM(c.cod_fab))
LEFT JOIN sal ON sal.cf = LTRIM(RTRIM(c.cod_fab))
LEFT JOIN dev ON dev.cf = LTRIM(RTRIM(c.cod_fab))
WHERE c.cant <> (ISNULL(ent.q,0) - ISNULL(sal.q,0) + ISNULL(dev.q,0))
ORDER BY ABS(c.cant - (ISNULL(ent.q,0) - ISNULL(sal.q,0) + ISNULL(dev.q,0))) DESC;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 6 — Variantes de moneda';
PRINT ' (MN vs MXN, etc.)';
PRINT '===========================================================';

SELECT ISNULL(moneda, '(NULL)') AS moneda, COUNT(*) AS veces
FROM inventario.catalogo_materiales
GROUP BY moneda
ORDER BY veces DESC;
GO

PRINT '===========================================================';
PRINT ' DIAGNÓSTICO 7 — Stock negativo (síntoma de descuadre)';
PRINT '===========================================================';

SELECT COUNT(*) AS materiales_negativos
FROM inventario.catalogo_materiales
WHERE cant < 0 AND LTRIM(RTRIM(cod_fab)) <> 'ND';
GO

PRINT '';
PRINT '===========================================================';
PRINT ' FIN DEL DIAGNÓSTICO — comparte los resultados de las 7';
PRINT ' secciones para definir los scripts de corrección.';
PRINT '===========================================================';
