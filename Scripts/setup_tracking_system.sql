-- =====================================================
-- Script Consolidado: Sistema de Rastreamento
-- Cria as 3 tabelas necessárias para o tracking
-- =====================================================

-- 1. Tabela de Interações com Comandos, Botões e Modais
CREATE TABLE IF NOT EXISTS CommandInteractions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(50) NOT NULL,
    UserId VARCHAR(50) NOT NULL COMMENT 'ID do usuário que executou a interação',
    InteractionType ENUM('COMMAND', 'BUTTON', 'MODAL') NOT NULL COMMENT 'Tipo de interação',
    Command VARCHAR(100) NULL COMMENT 'Comando executado (ex: !ajuda, !ping) - usado quando InteractionType = COMMAND',
    ActionId VARCHAR(100) NULL COMMENT 'ID da ação (ex: confirm_ai_yes, view_my_reminders) - usado para BUTTON/MODAL',
    Arguments TEXT NULL COMMENT 'Argumentos passados ao comando',
    Channel VARCHAR(50) NOT NULL COMMENT 'Canal/DM onde foi executado',
    ThreadTs VARCHAR(50) NULL COMMENT 'Thread do Slack',
    MessageTs VARCHAR(50) NOT NULL COMMENT 'Timestamp da mensagem',
    SourceIp VARCHAR(45) NULL COMMENT 'IP de origem (IPv4 ou IPv6)',
    UserAgent VARCHAR(500) NULL COMMENT 'User-Agent',
    Success BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a interação foi executada com sucesso',
    ErrorMessage TEXT NULL COMMENT 'Mensagem de erro se falhou',
    ExecutedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
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

-- 2. Tabela de Conversas com o Agente IA (contexto persistido)
CREATE TABLE IF NOT EXISTS AgentConversations (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    ThreadKey VARCHAR(191) NOT NULL UNIQUE COMMENT 'Chave única da thread (TeamId_UserId_Channel_ThreadTs) - 191 chars devido ao limite de índice UTF8MB4',
    TeamId VARCHAR(50) NOT NULL,
    Channel VARCHAR(50) NOT NULL COMMENT 'Canal onde a conversa acontece',
    ThreadTs VARCHAR(50) NULL COMMENT 'Timestamp da thread no Slack',
    UserId VARCHAR(50) NOT NULL COMMENT 'Usuário que iniciou/participa da conversa',
    ConversationHistory LONGTEXT NOT NULL COMMENT 'Histórico completo da conversa em formato JSON (compatível com MariaDB)',
    SummaryContext TEXT NULL COMMENT 'Resumo do contexto quando o histórico fica muito grande',
    MessageCount INT NOT NULL DEFAULT 0 COMMENT 'Quantidade total de mensagens trocadas',
    StartedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Quando a conversa começou',
    LastInteractionAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Última vez que houve interação',
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a conversa ainda está ativa',
    INDEX idx_thread_key (ThreadKey),
    INDEX idx_team_active (TeamId, IsActive),
    INDEX idx_user (UserId),
    INDEX idx_last_interaction (LastInteractionAt),
    INDEX idx_team_channel (TeamId, Channel),
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Contexto persistido de conversas com o agente IA';

-- 3. Tabela de Interações com o Agente IA (métricas)
CREATE TABLE IF NOT EXISTS AgentInteractions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(50) NOT NULL,
    UserId VARCHAR(50) NOT NULL COMMENT 'Usuário que interagiu com o agente',
    ThreadKey VARCHAR(191) NOT NULL COMMENT 'Referência à conversa - 191 chars devido ao limite de índice UTF8MB4',
    Channel VARCHAR(50) NOT NULL,
    ThreadTs VARCHAR(50) NULL,
    MessageTs VARCHAR(50) NOT NULL COMMENT 'Timestamp da mensagem do usuário',
    UserMessageLength INT NOT NULL DEFAULT 0 COMMENT 'Tamanho da mensagem do usuário em caracteres',
    AIProvider VARCHAR(50) NOT NULL COMMENT 'Nome do provider (OpenAI, Gemini, etc)',
    AIModel VARCHAR(100) NULL COMMENT 'Modelo específico usado (gpt-4, gemini-pro, etc)',
    ResponseLength INT NOT NULL DEFAULT 0 COMMENT 'Tamanho da resposta da IA em caracteres',
    ResponseTime INT NOT NULL DEFAULT 0 COMMENT 'Tempo de resposta em milissegundos',
    SourceIp VARCHAR(45) NULL COMMENT 'IP de origem',
    UserAgent VARCHAR(500) NULL COMMENT 'User-Agent',
    Success BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a interação foi bem-sucedida',
    ErrorMessage TEXT NULL COMMENT 'Mensagem de erro se falhou',
    TokensUsed INT NULL COMMENT 'Quantidade de tokens consumidos (se disponível)',
    EstimatedCost DECIMAL(10, 6) NULL COMMENT 'Custo estimado em USD',
    InteractedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_team_interacted (TeamId, InteractedAt),
    INDEX idx_user (UserId),
    INDEX idx_thread_key (ThreadKey),
    INDEX idx_provider (AIProvider),
    INDEX idx_model (AIModel),
    INDEX idx_interacted_at (InteractedAt),
    INDEX idx_team_provider (TeamId, AIProvider),
    INDEX idx_success (Success),
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Métricas e estatísticas de uso do agente IA para relatórios e análises';

-- =====================================================
-- Verificação: Listar as tabelas criadas
-- =====================================================
SELECT 
    'CommandInteractions' as TableName,
    COUNT(*) as RecordCount
FROM CommandInteractions
UNION ALL
SELECT 
    'AgentConversations' as TableName,
    COUNT(*) as RecordCount
FROM AgentConversations
UNION ALL
SELECT 
    'AgentInteractions' as TableName,
    COUNT(*) as RecordCount
FROM AgentInteractions;

-- =====================================================
-- FIM DO SCRIPT
-- =====================================================

