-- Script para criar tabela CommandInteractions
-- Sistema de rastreamento de interações com comandos, botões e modais

CREATE TABLE IF NOT EXISTS CommandInteractions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(50) NOT NULL,
    
    -- Identificação do usuário
    UserId VARCHAR(50) NOT NULL COMMENT 'ID do usuário que executou a interação',
    
    -- Tipo e identificação da interação
    InteractionType ENUM('COMMAND', 'BUTTON', 'MODAL') NOT NULL COMMENT 'Tipo de interação',
    Command VARCHAR(100) NULL COMMENT 'Comando executado (ex: !ajuda, !ping) - usado quando InteractionType = COMMAND',
    ActionId VARCHAR(100) NULL COMMENT 'ID da ação (ex: confirm_ai_yes, view_my_reminders) - usado para BUTTON/MODAL',
    Arguments TEXT NULL COMMENT 'Argumentos passados ao comando',
    
    -- Localização no Slack
    Channel VARCHAR(50) NOT NULL COMMENT 'Canal/DM onde foi executado',
    ThreadTs VARCHAR(50) NULL COMMENT 'Thread do Slack',
    MessageTs VARCHAR(50) NOT NULL COMMENT 'Timestamp da mensagem',
    
    -- Informações da requisição HTTP (para auditoria)
    SourceIp VARCHAR(45) NULL COMMENT 'IP de origem (IPv4 ou IPv6)',
    UserAgent VARCHAR(500) NULL COMMENT 'User-Agent',
    
    -- Resultado
    Success BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a interação foi executada com sucesso',
    ErrorMessage TEXT NULL COMMENT 'Mensagem de erro se falhou',
    
    -- Timestamp
    ExecutedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Índices para performance em relatórios
    INDEX idx_team_executed (TeamId, ExecutedAt),
    INDEX idx_interaction_type (InteractionType),
    INDEX idx_command (Command),
    INDEX idx_action_id (ActionId),
    INDEX idx_user (UserId),
    INDEX idx_channel (Channel),
    INDEX idx_executed_at (ExecutedAt),
    INDEX idx_team_type (TeamId, InteractionType),
    
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Rastreamento completo de interações com comandos, botões e modais';

