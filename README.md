# Portfolio — Lojas & Pedidos

API REST para gestão de lojas, catálogo, estoque e pedidos. O projeto foi desenvolvido para representar o núcleo de um sistema comercial multiempresa: cada usuário acessa somente os dados da loja à qual está vinculado.

## O desafio

Pequenos negócios precisam concentrar o cadastro de produtos, o controle de estoque e a criação de pedidos em uma única aplicação. Esta API organiza esse fluxo com autenticação, regras de acesso e persistência relacional, mantendo os dados separados por loja.

## Principais funcionalidades

- Cadastro e autenticação de usuários com sessões por JWT.
- Criação e administração de lojas, incluindo vínculo de usuários e papéis de acesso.
- Catálogo de produtos com preços, dados fiscais, saldo em estoque e até duas imagens por item.
- Importação de produtos a partir de planilhas.
- Criação, edição e cancelamento de pedidos com atualização consistente do estoque.
- Histórico de pedidos com busca por cliente, período ou identificadores.
- Listagens paginadas para produtos e pedidos, preparadas para catálogos maiores.
- Endpoint de saúde para acompanhamento da disponibilidade da aplicação.

## Decisões técnicas em destaque

- **Isolamento multiempresa:** as consultas de produtos e pedidos são filtradas pela loja presente no token do usuário, reduzindo o risco de acesso cruzado entre operações.
- **Integridade de estoque:** alterações e cancelamentos de pedidos recalculam as quantidades dos itens envolvidos, evitando divergências no saldo.
- **Autenticação segura:** senhas são armazenadas com hash BCrypt e a API usa tokens JWT com emissor, público e validade configuráveis.
- **Persistência evolutiva:** o banco é modelado com Entity Framework Core e migrations versionadas, facilitando a evolução do esquema.
- **Armazenamento de mídia desacoplado:** imagens de produtos são tratadas por uma abstração de armazenamento compatível com S3, com suporte a Cloudflare R2.

## Stack

- C# e ASP.NET Core (.NET 10)
- Entity Framework Core
- PostgreSQL e Npgsql
- JWT Bearer Authentication
- BCrypt
- AWS SDK S3 / Cloudflare R2
- OpenAPI para documentação da API em desenvolvimento

## Estrutura do projeto

```text
backend/
├── Controllers/      # Endpoints de autenticação, lojas, produtos e pedidos
├── Data/             # DbContext, inicialização e dados de apoio
├── Dtos/             # Contratos de entrada e saída da API
├── Models/           # Entidades de domínio e relacionamentos
├── Services/         # Contexto do usuário, importação e armazenamento de imagens
└── Migrations/       # Histórico versionado do banco de dados
```

## O que este projeto demonstra

Este repositório evidencia experiência prática na construção de APIs para regras de negócio reais: modelagem relacional, controle de acesso, separação por tenant, tratamento de fluxos transacionais de estoque, integração com serviços de armazenamento e preocupação com escalabilidade de consultas.

---

Desenvolvido como projeto de portfólio para demonstrar competências em desenvolvimento backend com .NET.
