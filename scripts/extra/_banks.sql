/* EXTRA_BANKS */
-- Справочник банков для extra-выгрузки: NrBank + читаемое имя.
-- Зарезервированное имя (_banks.sql) — не является data-скриптом.
SELECT NrBank, BankName
FROM dbo.BanksDirectory
ORDER BY BankName;
