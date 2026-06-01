/* EXTRA_UNLOAD */
-- Atomic-версия PAYMENTS: фильтр по выбранным банкам через плейсхолдер {banks}.
-- {banks} подставляется как список строк в кавычках, напр. 'B01','B02'.
SELECT NrBank, LineFile
FROM dbo.PaymentsExport
WHERE NrBank IN ({banks})
ORDER BY NrBank;
