-- Script para criar tabela WorkspaceSettings
-- Armazena configurações do workspace, incluindo configuração de relatórios

CREATE TABLE IF NOT EXISTS WorkspaceSettings (
    TeamId VARCHAR(50) PRIMARY KEY,
    AdminUserId VARCHAR(50) NOT NULL COMMENT 'User ID do administrador que instalou o bot',
    
    -- Configurações de relatórios (desabilitados por padrão)
    DailyReportEnabled BOOLEAN DEFAULT FALSE COMMENT 'Enviar relatório diário',
    DailyReportTime TIME DEFAULT '09:00:00' COMMENT 'Horário do relatório diário',
    
    WeeklyReportEnabled BOOLEAN DEFAULT FALSE COMMENT 'Enviar relatório semanal',
    WeeklyReportDay TINYINT DEFAULT 1 COMMENT 'Dia da semana (0=Domingo, 1=Segunda, etc)',
    WeeklyReportTime TIME DEFAULT '09:00:00' COMMENT 'Horário do relatório semanal',
    
    MonthlyReportEnabled BOOLEAN DEFAULT FALSE COMMENT 'Enviar relatório mensal',
    MonthlyReportDay TINYINT DEFAULT 1 COMMENT 'Dia do mês (1-28)',
    MonthlyReportTime TIME DEFAULT '09:00:00' COMMENT 'Horário do relatório mensal',
    
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Configurações do workspace e relatórios automáticos';


