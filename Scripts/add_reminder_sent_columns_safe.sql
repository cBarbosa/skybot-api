-- Script SEGURO para adicionar colunas IsSent e SentAt
-- Compatível com MySQL e MariaDB
-- Este script verifica se as colunas existem antes de criar

-- Verifica e adiciona IsSent
SELECT COUNT(*) INTO @col_exists
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'Reminders'
  AND COLUMN_NAME = 'IsSent';

SET @sql = IF(@col_exists = 0,
  'ALTER TABLE Reminders ADD COLUMN IsSent BOOLEAN DEFAULT FALSE',
  'SELECT "Coluna IsSent já existe" AS message');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Verifica e adiciona SentAt
SELECT COUNT(*) INTO @col_exists
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'Reminders'
  AND COLUMN_NAME = 'SentAt';

SET @sql = IF(@col_exists = 0,
  'ALTER TABLE Reminders ADD COLUMN SentAt DATETIME NULL',
  'SELECT "Coluna SentAt já existe" AS message');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Atualiza registros antigos
UPDATE Reminders SET IsSent = FALSE WHERE IsSent IS NULL;

-- Verifica e cria índice
SELECT COUNT(*) INTO @idx_exists
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'Reminders'
  AND INDEX_NAME = 'idx_reminders_pending';

SET @sql = IF(@idx_exists = 0,
  'CREATE INDEX idx_reminders_pending ON Reminders(DueDate, IsSent)',
  'SELECT "Índice já existe" AS message');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

