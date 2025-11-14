-- Script para criar tabela AgentConversations
-- Armazena o contexto e histórico de conversas com o agente IA

CREATE TABLE IF NOT EXISTS AgentConversations (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Identificação única da thread
    ThreadKey VARCHAR(191) NOT NULL UNIQUE COMMENT 'Chave única da thread (TeamId_Channel_ThreadTs)',
    TeamId VARCHAR(50) NOT NULL,
    Channel VARCHAR(50) NOT NULL COMMENT 'Canal onde a conversa acontece',
    ThreadTs VARCHAR(50) NULL COMMENT 'Timestamp da thread no Slack',
    UserId VARCHAR(50) NOT NULL COMMENT 'Usuário que iniciou/participa da conversa',
    
    -- Contexto da conversa
    ConversationHistory LONGTEXT NOT NULL COMMENT 'Histórico completo da conversa em formato JSON',
    SummaryContext TEXT NULL COMMENT 'Resumo do contexto quando o histórico fica muito grande',
    MessageCount INT NOT NULL DEFAULT 0 COMMENT 'Quantidade total de mensagens trocadas',
    
    -- Controle de timestamps
    StartedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Quando a conversa começou',
    LastInteractionAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Última vez que houve interação',
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    -- Status
    IsActive BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a conversa ainda está ativa',
    
    -- Índices para performance
    INDEX idx_thread_key (ThreadKey),
    INDEX idx_team_active (TeamId, IsActive),
    INDEX idx_user (UserId),
    INDEX idx_last_interaction (LastInteractionAt),
    INDEX idx_team_channel (TeamId, Channel),
    
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Contexto persistido de conversas com o agente IA';

