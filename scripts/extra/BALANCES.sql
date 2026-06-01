/* EXTRA_UNLOAD */
-- Базовый extra-скрипт (все банки). Должен вернуть колонки NrBank и LineFile.
SELECT NrBank, LineFile
FROM dbo.BalancesExport
ORDER BY NrBank;
