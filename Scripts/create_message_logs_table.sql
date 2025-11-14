-- Script para criar tabela MessageLogs
-- Sistema de auditoria e registro de mensagens enviadas pelo bot

CREATE TABLE IF NOT EXISTS MessageLogs (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(50) NOT NULL,
    
    -- Informações da mensagem
    MessageTs VARCHAR(50) NOT NULL COMMENT 'Timestamp da mensagem no Slack (identificador único)',
    Channel VARCHAR(50) NOT NULL COMMENT 'ID do canal/usuário/grupo de destino',
    DestinationType ENUM('CHANNEL', 'USER', 'GROUP') NOT NULL,
    ThreadTs VARCHAR(50) NULL COMMENT 'Timestamp da thread (se for resposta)',
    
    -- Informações do emissor (quem enviou via API)
    ApiKeyId INT NULL COMMENT 'ID da API Key usada para enviar',
    ApiKeyName VARCHAR(200) NULL COMMENT 'Nome descritivo da API Key',
    
    -- Informações da requisição HTTP (para auditoria e segurança)
    SourceIp VARCHAR(45) NULL COMMENT 'IP de origem da conexão direta (IPv4 ou IPv6)',
    ForwardedFor VARCHAR(200) NULL COMMENT 'IP real do cliente (X-Forwarded-For, de trás de proxy/LB)',
    UserAgent VARCHAR(500) NULL COMMENT 'User-Agent (navegador, app, ferramenta)',
    Referer VARCHAR(500) NULL COMMENT 'URL de origem da requisição',
    RequestId VARCHAR(100) NULL COMMENT 'ID único da requisição (para rastreamento)',
    
    -- Tipo de conteúdo
    ContentType ENUM('TEXT', 'BLOCKS') NOT NULL,
    HasAttachments BOOLEAN DEFAULT FALSE,
    
    -- Metadata
    SentAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Status ENUM('SENT', 'DELETED', 'FAILED') DEFAULT 'SENT',
    DeletedAt DATETIME NULL,
    ErrorMessage TEXT NULL,
    
    -- Índices para performance em relatórios
    INDEX idx_team_sent_at (TeamId, SentAt),
    INDEX idx_message_ts (MessageTs),
    INDEX idx_channel (Channel),
    INDEX idx_status (Status),
    INDEX idx_api_key (ApiKeyId),
    INDEX idx_source_ip (SourceIp),
    INDEX idx_request_id (RequestId),
    
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Log completo de auditoria de mensagens enviadas pelo bot';

