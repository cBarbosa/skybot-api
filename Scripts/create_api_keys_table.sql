-- Script para criar tabela ApiKeys
-- Sistema de autenticação via API Key para workspaces externos

CREATE TABLE IF NOT EXISTS ApiKeys (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(50) NOT NULL,
    ApiKey VARCHAR(100) NOT NULL UNIQUE,
    Name VARCHAR(200) NOT NULL COMMENT 'Nome descritivo da chave (ex: "Integração Produção")',
    IsActive BOOLEAN DEFAULT TRUE,
    AllowedEndpoints TEXT NULL COMMENT 'Array de endpoints permitidos em JSON. NULL = todos permitidos',
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastUsedAt DATETIME NULL,
    ExpiresAt DATETIME NULL,
    
    INDEX idx_api_key (ApiKey),
    INDEX idx_team_id (TeamId),
    INDEX idx_is_active (IsActive),
    FOREIGN KEY (TeamId) REFERENCES SlackTokens(TeamId) ON DELETE CASCADE
) COMMENT='Chaves de API para autenticação de workspaces externos';

