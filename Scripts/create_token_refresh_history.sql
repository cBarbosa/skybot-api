-- Cria tabela para histórico de refresh tokens
-- Armazena todas as tentativas de renovação de tokens

CREATE TABLE IF NOT EXISTS SlackTokenRefreshHistory (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TeamId VARCHAR(255) NOT NULL,
    RefreshToken VARCHAR(500) NOT NULL,
    Success BOOLEAN NOT NULL,
    ErrorMessage TEXT NULL,
    RefreshedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    OldAccessToken VARCHAR(50) NULL COMMENT 'Últimos caracteres do token antigo para auditoria',
    NewAccessToken VARCHAR(50) NULL COMMENT 'Últimos caracteres do token novo para auditoria',
    INDEX idx_team_id (TeamId),
    INDEX idx_refreshed_at (RefreshedAt),
    INDEX idx_success (Success)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

