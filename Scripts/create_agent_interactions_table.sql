-- Script para criar tabela AgentInteractions
-- Armazena métricas e estatísticas de uso do agente IA

CREATE TABLE IF NOT EXISTS AgentInteractions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Identificação
    TeamId VARCHAR(50) NOT NULL,
    UserId VARCHAR(50) NOT NULL COMMENT 'Usuário que interagiu com o agente',
    ThreadKey VARCHAR(191) NOT NULL COMMENT 'Referência à conversa',
    
    -- Localização no Slack
    Channel VARCHAR(50) NOT NULL,
    ThreadTs VARCHAR(50) NULL,
    MessageTs VARCHAR(50) NOT NULL COMMENT 'Timestamp da mensagem do usuário',
    
    -- Informações sobre a mensagem do usuário
    UserMessageLength INT NOT NULL DEFAULT 0 COMMENT 'Tamanho da mensagem do usuário em caracteres',
    
    -- Informações sobre o provedor de IA
    AIProvider VARCHAR(50) NOT NULL COMMENT 'Nome do provider (OpenAI, Gemini, etc)',
    AIModel VARCHAR(100) NULL COMMENT 'Modelo específico usado (gpt-4, gemini-pro, etc)',
    
    -- Métricas de resposta
    ResponseLength INT NOT NULL DEFAULT 0 COMMENT 'Tamanho da resposta da IA em caracteres',
    ResponseTime INT NOT NULL DEFAULT 0 COMMENT 'Tempo de resposta em milissegundos',
    
    -- Informações da requisição HTTP (auditoria)
    SourceIp VARCHAR(45) NULL COMMENT 'IP de origem',
    UserAgent VARCHAR(500) NULL COMMENT 'User-Agent',
    
    -- Resultado
    Success BOOLEAN NOT NULL DEFAULT TRUE COMMENT 'Se a interação foi bem-sucedida',
    ErrorMessage TEXT NULL COMMENT 'Mensagem de erro se falhou',
    
    -- Custos (opcional, para tracking de uso de APIs pagas)
    TokensUsed INT NULL COMMENT 'Quantidade de tokens consumidos (se disponível)',
    EstimatedCost DECIMAL(10, 6) NULL COMMENT 'Custo estimado em USD',
    
    -- Timestamp
    InteractedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Índices para relatórios e análises
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

