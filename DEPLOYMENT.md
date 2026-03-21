# Guia de Deployment - Document AI Service

## Visão Geral

Este documento fornece instruções para fazer deploy do Document AI Service em diferentes ambientes.

## Pré-requisitos

### Sistema Operacional
- Linux (Ubuntu 20.04+, Debian 11+)
- macOS 11+
- Windows 10+ (com WSL2)

### Dependências
- .NET 8.0 SDK ou Runtime
- Tesseract OCR 4.1.1+
- Git
- Docker (opcional, para containerização)

## Instalação de Dependências

### Ubuntu/Debian

```bash
# Atualizar pacotes
sudo apt-get update

# Instalar .NET SDK 8.0
sudo apt-get install -y dotnet-sdk-8.0

# Instalar Tesseract OCR
sudo apt-get install -y tesseract-ocr libtesseract-dev

# Verificar instalações
dotnet --version
tesseract --version
```

### macOS

```bash
# Instalar via Homebrew
brew install dotnet tesseract

# Verificar instalações
dotnet --version
tesseract --version
```

### Windows

1. Baixar .NET 8.0 SDK: https://dotnet.microsoft.com/download
2. Instalar Tesseract via chocolatey:
   ```powershell
   choco install tesseract
   ```

## Deployment Local

### 1. Clonar Repositório

```bash
cd /home/ubuntu
git clone <repository-url> document-ai-service
cd document-ai-service/DocumentAIService
```

### 2. Restaurar Dependências

```bash
dotnet restore
```

### 3. Compilar Aplicação

```bash
# Debug
dotnet build

# Release
dotnet build -c Release
```

### 4. Executar Aplicação

```bash
# Desenvolvimento
dotnet run

# Produção (Release)
dotnet run -c Release --urls "http://0.0.0.0:5000"
```

### 5. Verificar Saúde

```bash
curl http://localhost:5000/api/validation/health
```

## Deployment em Produção

### 1. Preparar Build Release

```bash
cd /home/ubuntu/document-ai-service/DocumentAIService

# Limpar build anterior
dotnet clean

# Compilar em modo Release
dotnet build -c Release

# Publicar
dotnet publish -c Release -o ./publish
```

### 2. Configurar Serviço Systemd (Linux)

Criar arquivo `/etc/systemd/system/document-ai.service`:

```ini
[Unit]
Description=Document AI Service
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/opt/document-ai-service
ExecStart=/usr/bin/dotnet /opt/document-ai-service/DocumentAIService.dll --urls "http://0.0.0.0:5000"
Restart=always
RestartSec=10
SyslogIdentifier=document-ai

[Install]
WantedBy=multi-user.target
```

Ativar serviço:

```bash
# Copiar arquivos publicados
sudo mkdir -p /opt/document-ai-service
sudo cp -r publish/* /opt/document-ai-service/

# Ativar serviço
sudo systemctl daemon-reload
sudo systemctl enable document-ai
sudo systemctl start document-ai

# Verificar status
sudo systemctl status document-ai
```

### 3. Configurar Nginx como Reverse Proxy

Criar arquivo `/etc/nginx/sites-available/document-ai`:

```nginx
upstream document_ai {
    server localhost:5000;
}

server {
    listen 80;
    server_name api.example.com;

    # Redirecionar HTTP para HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name api.example.com;

    # Certificados SSL
    ssl_certificate /etc/letsencrypt/live/api.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.example.com/privkey.pem;

    # Configurações SSL
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    # Logging
    access_log /var/log/nginx/document-ai-access.log;
    error_log /var/log/nginx/document-ai-error.log;

    # Proxy
    location / {
        proxy_pass http://document_ai;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # Limitar tamanho de upload
    client_max_body_size 5M;
}
```

Ativar site:

```bash
sudo ln -s /etc/nginx/sites-available/document-ai /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

## Deployment com Docker

### 1. Criar Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar arquivos de projeto
COPY ["DocumentAIService.csproj", "."]
RUN dotnet restore "DocumentAIService.csproj"

# Copiar código
COPY . .
RUN dotnet build "DocumentAIService.csproj" -c Release -o /app/build

# Publicar
FROM build AS publish
RUN dotnet publish "DocumentAIService.csproj" -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y tesseract-ocr && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "DocumentAIService.dll"]
```

### 2. Criar docker-compose.yml

```yaml
version: '3.8'

services:
  document-ai:
    build: .
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:5000
    volumes:
      - ./logs:/app/logs
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/api/validation/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s

  nginx:
    image: nginx:latest
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./ssl:/etc/nginx/ssl:ro
    depends_on:
      - document-ai
    restart: unless-stopped
```

### 3. Build e Deploy com Docker

```bash
# Build imagem
docker-compose build

# Iniciar containers
docker-compose up -d

# Ver logs
docker-compose logs -f document-ai

# Parar containers
docker-compose down
```

## Deployment em Cloud

### AWS EC2

```bash
# 1. Criar instância EC2 (Ubuntu 22.04)
# 2. SSH para instância
ssh -i key.pem ubuntu@instance-ip

# 3. Instalar dependências
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0 tesseract-ocr git

# 4. Clonar repositório
git clone <repository-url>
cd document-ai-service/DocumentAIService

# 5. Publicar
dotnet publish -c Release -o ./publish

# 6. Configurar como serviço (ver seção Systemd acima)
```

### Azure App Service

```bash
# 1. Criar App Service
az appservice plan create --name document-ai-plan --resource-group mygroup --sku B1 --is-linux

az webapp create --resource-group mygroup --plan document-ai-plan --name document-ai --runtime "DOTNET|8.0"

# 2. Deploy via Git
git remote add azure <azure-git-url>
git push azure main
```

### Google Cloud Run

```bash
# 1. Criar Dockerfile (ver seção Docker acima)

# 2. Deploy
gcloud run deploy document-ai \
  --source . \
  --platform managed \
  --region us-central1 \
  --memory 512Mi \
  --timeout 60
```

## Monitoramento e Manutenção

### Logs

```bash
# Ver logs do serviço
sudo journalctl -u document-ai -f

# Ver logs do Nginx
sudo tail -f /var/log/nginx/document-ai-access.log
sudo tail -f /var/log/nginx/document-ai-error.log
```

### Backup

```bash
# Backup de configuração
tar -czf document-ai-backup-$(date +%Y%m%d).tar.gz /opt/document-ai-service

# Restaurar backup
tar -xzf document-ai-backup-20260321.tar.gz -C /opt
```

### Atualização

```bash
# 1. Parar serviço
sudo systemctl stop document-ai

# 2. Atualizar código
cd /home/ubuntu/document-ai-service
git pull origin main

# 3. Compilar
dotnet publish -c Release -o ./publish

# 4. Copiar para produção
sudo cp -r publish/* /opt/document-ai-service/

# 5. Reiniciar serviço
sudo systemctl start document-ai
```

## Checklist de Deployment

- [ ] Dependências instaladas
- [ ] Código clonado
- [ ] Build compilado com sucesso
- [ ] Testes passando
- [ ] Configuração de produção definida
- [ ] Certificados SSL configurados
- [ ] Reverse proxy configurado
- [ ] Serviço systemd criado
- [ ] Health check funcionando
- [ ] Logs configurados
- [ ] Backup configurado
- [ ] Monitoramento ativo
- [ ] Documentação atualizada

## Troubleshooting

### Aplicação não inicia

```bash
# Verificar logs
sudo journalctl -u document-ai -n 50

# Verificar porta em uso
sudo lsof -i :5000

# Verificar permissões
sudo chown -R www-data:www-data /opt/document-ai-service
```

### Tesseract não encontrado

```bash
# Verificar instalação
which tesseract
tesseract --version

# Reinstalar
sudo apt-get install --reinstall tesseract-ocr
```

### Erro de memória

```bash
# Aumentar limite de memória
# No arquivo de serviço, adicionar:
# Environment="DOTNET_GCHeapHardLimit=1073741824"
```

## Performance

### Otimizações

1. **Compilar em Release**: `dotnet publish -c Release`
2. **Usar CDN** para arquivos estáticos
3. **Cache de resultados**: Implementar Redis
4. **Load balancing**: Usar Nginx ou HAProxy
5. **Compressão**: Ativar gzip no Nginx

### Métricas

```bash
# Monitorar CPU e memória
top -p $(pgrep -f dotnet)

# Monitorar conexões
netstat -an | grep :5000
```

## Segurança

- [ ] HTTPS/SSL configurado
- [ ] Firewall configurado
- [ ] Autenticação implementada
- [ ] Rate limiting ativo
- [ ] Logs auditados
- [ ] Backup regular
- [ ] Atualizações de segurança aplicadas

---

**Versão**: 1.0.0  
**Data**: Março 2026
