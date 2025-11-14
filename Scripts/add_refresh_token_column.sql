-- Adiciona coluna RefreshToken na tabela SlackTokens
-- Permite NULL para tokens antigos que não têm refresh token

ALTER TABLE SlackTokens 
ADD COLUMN RefreshToken VARCHAR(500) NULL AFTER AccessToken;

-- Adiciona índice para melhorar performance em buscas
-- Nota: MySQL não suporta índices parciais com WHERE, então criamos índice normal
CREATE INDEX idx_refresh_token ON SlackTokens(RefreshToken);

