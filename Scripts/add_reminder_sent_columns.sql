-- Script para adicionar colunas IsSent e SentAt na tabela Reminders
-- Compatível com MySQL e MariaDB
-- Se as colunas já existirem, você verá um erro - isso é normal, pode ignorar

-- Adiciona coluna IsSent
ALTER TABLE Reminders 
ADD COLUMN IsSent BOOLEAN DEFAULT FALSE;

-- Adiciona coluna SentAt
ALTER TABLE Reminders 
ADD COLUMN SentAt DATETIME NULL;

-- Atualiza registros antigos para IsSent = FALSE
UPDATE Reminders SET IsSent = FALSE WHERE IsSent IS NULL;

-- Cria índice para melhor performance (se já existir, dará erro - pode ignorar)
CREATE INDEX idx_reminders_pending ON Reminders(DueDate, IsSent);
