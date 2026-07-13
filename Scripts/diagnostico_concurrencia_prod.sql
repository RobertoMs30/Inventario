-- ============================================================
--  DIAGNÓSTICO DE CONCURRENCIA / INTEGRIDAD — PRODUCCIÓN
--  SOLO LECTURA. No modifica nada. Seguro de ejecutar.
--  Objetivo: confirmar los hechos estructurales antes de
--  corregir el descuadre de stock y/o el folio cod_int.
--  Ejecutar en la base de PRODUCCIÓN (Results to Grid).
-- ============================================================
SET NOCOUNT ON;

PRINT '===========================================================';
PRINT ' 1 — ¿id es IDENTITY y PK? (define si hay pérdida de datos)';
PRINT '===========================================================';
SELECT
    t.name                AS tabla,
    c.name                AS columna,
    c.is_identity         AS es_identity,
    ty.name               AS tipo,
    c.max_length          AS longitud
FROM sys.columns c
JOIN sys.tables  t  ON t.object_id = c.object_id
JOIN sys.types   ty ON ty.user_type_id = c.user_type_id
WHERE t.object_id IN (
        OBJECT_ID('inventario.entradas_inventario'),
        OBJECT_ID('inventario.salidas_inventario'))
  AND c.name IN ('id','cod_int','cod_fab')
ORDER BY t.name, c.column_id;

SELECT
    OBJECT_NAME(i.object_id) AS tabla,
    i.name                   AS indice,
    i.is_primary_key         AS es_pk,
    i.is_unique              AS es_unico,
    STRING_AGG(col.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columnas
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns col       ON col.object_id=ic.object_id AND col.column_id=ic.column_id
WHERE i.object_id IN (
        OBJECT_ID('inventario.entradas_inventario'),
        OBJECT_ID('inventario.salidas_inventario'))
  AND i.type > 0
GROUP BY i.object_id, i.name, i.is_primary_key, i.is_unique;

PRINT '===========================================================';
PRINT ' 2 — Lógica REAL del SP de salida en producción';
PRINT '     Busca: "SET cant = @balance" (absoluto = BUG)';
PRINT '     vs.    "SET cant = cant -"   (relativo = correcto)';
PRINT '===========================================================';
SELECT
    CASE
        WHEN definition LIKE '%cant = @balance%'      THEN 'ABSOLUTO (vulnerable a lost update)'
        WHEN definition LIKE '%cant = ISNULL(cant%'   THEN 'RELATIVO (ok)'
        WHEN definition LIKE '%cant = cant -%'        THEN 'RELATIVO (ok)'
        ELSE 'REVISAR MANUALMENTE'
    END AS patron_update_stock,
    CASE WHEN definition LIKE '%UPDLOCK%' OR definition LIKE '%SERIALIZABLE%'
         THEN 'CON bloqueo' ELSE 'SIN bloqueo explícito' END AS bloqueo
FROM sys.sql_modules
WHERE object_id = OBJECT_ID('dbo.sp_RegistrarSalidaMaterial');

PRINT '===========================================================';
PRINT ' 3 — Descuadre de stock: cant vs movimientos (Top 30)';
PRINT '     Esperado = entradas - salidas + devoluciones';
PRINT '===========================================================';
WITH ent AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.entradas_inventario     GROUP BY LTRIM(RTRIM(cod_fab))),
     sal AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.salidas_inventario      GROUP BY LTRIM(RTRIM(cod_fab))),
     dev AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.devoluciones_inventario GROUP BY LTRIM(RTRIM(cod_fab)))
SELECT TOP 30
    LTRIM(RTRIM(c.cod_fab))                                          AS cod_fab,
    c.cant                                                          AS cant_actual,
    ISNULL(ent.q,0)-ISNULL(sal.q,0)+ISNULL(dev.q,0)                AS cant_esperada,
    c.cant-(ISNULL(ent.q,0)-ISNULL(sal.q,0)+ISNULL(dev.q,0))       AS descuadre
FROM inventario.catalogo_materiales c
LEFT JOIN ent ON ent.cf=LTRIM(RTRIM(c.cod_fab))
LEFT JOIN sal ON sal.cf=LTRIM(RTRIM(c.cod_fab))
LEFT JOIN dev ON dev.cf=LTRIM(RTRIM(c.cod_fab))
WHERE c.cant <> (ISNULL(ent.q,0)-ISNULL(sal.q,0)+ISNULL(dev.q,0))
ORDER BY ABS(c.cant-(ISNULL(ent.q,0)-ISNULL(sal.q,0)+ISNULL(dev.q,0))) DESC;

-- Resumen del descuadre
WITH ent AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.entradas_inventario     GROUP BY LTRIM(RTRIM(cod_fab))),
     sal AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.salidas_inventario      GROUP BY LTRIM(RTRIM(cod_fab))),
     dev AS (SELECT LTRIM(RTRIM(cod_fab)) cf, SUM(cantidad) q FROM inventario.devoluciones_inventario GROUP BY LTRIM(RTRIM(cod_fab)))
SELECT
    COUNT(*) AS materiales_descuadrados,
    SUM(CASE WHEN c.cant < 0 THEN 1 ELSE 0 END) AS materiales_con_stock_negativo
FROM inventario.catalogo_materiales c
LEFT JOIN ent ON ent.cf=LTRIM(RTRIM(c.cod_fab))
LEFT JOIN sal ON sal.cf=LTRIM(RTRIM(c.cod_fab))
LEFT JOIN dev ON dev.cf=LTRIM(RTRIM(c.cod_fab))
WHERE c.cant <> (ISNULL(ent.q,0)-ISNULL(sal.q,0)+ISNULL(dev.q,0));

PRINT '===========================================================';
PRINT ' 4 — cod_int: ¿cuántos duplicados hay HOY en producción?';
PRINT '     (define si la Opción B necesita limpieza previa)';
PRINT '===========================================================';
SELECT
    (SELECT COUNT(*) FROM (
        SELECT LTRIM(RTRIM(cod_int)) ci FROM inventario.entradas_inventario
        GROUP BY LTRIM(RTRIM(cod_int)) HAVING COUNT(*)>1) d)        AS grupos_cod_int_duplicados,
    (SELECT COUNT(*) FROM inventario.entradas_inventario
        WHERE TRY_CAST(cod_int AS BIGINT) IS NULL
          AND LTRIM(RTRIM(ISNULL(cod_int,'')))<>'')                 AS cod_int_no_numericos,
    (SELECT COUNT(*) FROM inventario.entradas_inventario)              AS total_entradas;

PRINT '';
PRINT ' FIN — comparte los 4 resultados para definir la corrección.';
