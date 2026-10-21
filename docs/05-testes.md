# 05 — Plano de testes

## 1. Escopo e estratégia

| Tipo | Ferramenta | O que cobre | Onde |
|---|---|---|---|
| **Unitário** | xUnit | Regras do domínio (pedido, carrinho, CPF, TOTP, assinatura do webhook, validação de imagem, leitura das APIs) | `tests/PortalVendas.Tests/Dominio`, `Infraestrutura` |
| **Integração** | xUnit + EF Core SQLite em memória | Serviços reais da aplicação com banco real (transações, `UPDATE` atômico de estoque, idempotência, concorrência otimista) e integrações falsas (Mercado Pago, Melhor Envio, e-mail) | `tests/PortalVendas.Tests/Aplicacao` |
| **Ponta a ponta (manual)** | Navegador + sandbox do Mercado Pago e do Melhor Envio | Fluxo de compra completo, telas, webhook real | Roteiro na seção 4 |
| **Segurança** | Testes automatizados + roteiro manual | IDOR, CSRF, XSS, injeção, força bruta, upload, assinatura do webhook | Seções 3 e 5 |
| **Desempenho** | Roteiro com k6 ou JMeter | RNF01 | Seção 6 |

São **97 casos automatizados** (70 `[Fact]` e 7 `[Theory]` com 27 variações).

```powershell
dotnet test PortalVendas.sln
# com cobertura (relatório em TestResults\)
dotnet test PortalVendas.sln --collect:"XPlat Code Coverage"
```

**Critério de entrada:** a solução compila sem erros. **Critério de saída (Portão 5):** 100% dos testes passando, zero bugs críticos ou altos abertos e o roteiro manual da seção 4 executado em homologação.

> Os testes de integração rodam em SQLite para não depender de um servidor. O que é específico do SQL Server (collation sem acento, índices filtrados, CHECK constraints) é verificado no roteiro manual em homologação com o banco real.

## 2. Matriz de rastreabilidade

| Requisito | Casos automatizados | Arquivo |
|---|---|---|
| RF02 busca sem acento | Manual (collation SQL Server) — roteiro M03 | — |
| RF04 carrinho limitado ao estoque, 10 por item, mesclagem | `Limita_ao_estoque_disponivel`, `Desconta_o_que_esta_reservado`, `Limita_a_dez_unidades_por_produto`, `Mesclar_mantem_a_maior_quantidade_sem_duplicar`, `Carrinho_anonimo_e_mesclado_no_login` | CarrinhoTests, CheckoutServiceTests |
| RF05 cadastro, CPF, e-mail duplicado, bloqueio | `Cpf_valida_digitos_verificadores`, `Cadastro_cria_conta_nao_confirmada_e_envia_link`, `Cadastro_valida_cpf`, `Email_ja_cadastrado_responde_igual_e_avisa_o_dono`, `Bloqueia_por_15_minutos_apos_5_erros`, `Email_inexistente_tem_a_mesma_resposta_de_senha_errada`, `Exige_email_confirmado` | DocumentosTests, ContaServiceTests, CheckoutServiceTests |
| RF06 redefinição de senha | `Redefinicao_de_senha_troca_o_carimbo_e_o_link_vale_uma_vez`, `Link_de_redefinicao_expira_em_1_hora` | ContaServiceTests |
| RF07 endereço / CEP | `Cep_valida_formato`, `Nao_usa_endereco_de_outro_cliente` | DocumentosTests, CheckoutServiceTests |
| RF08 frete por peso e medidas, manuseio, grátis, contingência | `Cota_no_provedor_e_soma_dias_de_manuseio`, `Frete_gratis_na_opcao_mais_barata`, `Sem_frete_gratis_abaixo_do_minimo`, `Usa_contingencia_quando_o_provedor_cai`, `Cep_nao_atendido`, `Cep_invalido_nem_consulta_o_provedor`, `Le_cotacoes_e_ignora_servicos_com_erro`, `Monta_corpo_com_peso_medidas_e_valor_declarado`, `Recusa_opcao_de_frete_inexistente` | FreteServiceTests, IntegracoesTests, CheckoutServiceTests |
| RF09 pedido, reserva, corrida pela última unidade, duplo clique | `Cria_pedido_reserva_estoque_e_esvazia_carrinho`, `Duplo_clique_cria_um_unico_pedido`, `Ultima_unidade_vendida_para_outro_cliente_no_meio_do_checkout`, `Reserva_atomica_nunca_passa_do_estoque` | CheckoutServiceTests |
| RF10 Pix | `Pix_gerado_com_validade_de_30_minutos`, `Reaproveita_pix_vigente_em_vez_de_gerar_outro`, `Le_resposta_de_pix` | PagamentoServiceTests, IntegracoesTests |
| RF11 cartão | `Cartao_aprovado_na_hora`, `Cartao_recusado_explica_o_motivo_e_pedido_continua_aguardando`, `Cartao_em_analise_coloca_pedido_em_analise`, `Parcelas_respeitam_a_parcela_minima`, `Le_resposta_de_boleto_e_cartao` | PagamentoServiceTests, IntegracoesTests |
| RF12 boleto | `Le_resposta_de_boleto_e_cartao`, `Dias_uteis_pulam_fim_de_semana` + roteiro M07 | IntegracoesTests, DocumentosTests |
| RF13 webhook | `Webhook_aprovado_confirma_pedido_e_baixa_estoque`, `Webhook_repetido_e_processado_uma_vez`, `Webhook_com_assinatura_invalida_e_registrado_mas_ignorado`, `Aceita_assinatura_correta`, `Recusa_assinatura_adulterada_ou_ausente`, `Id_alfanumerico_vai_em_minusculas` | PagamentoServiceTests, SegurancaTests |
| RF14 meus pedidos | `Cliente_nao_ve_pedido_de_outro_cliente` | PedidoServiceTests |
| RF16 cancelamento | `Cancelar_antes_de_pagar_libera_a_reserva`, `Cancelar_pedido_pago_estorna_e_devolve_estoque`, `Falha_no_estorno_mantem_o_pedido_como_estava`, `Pedido_enviado_nao_pode_ser_cancelado` | PedidoServiceTests, PedidoTests |
| RF17 upload de imagem | `Aceita_jpg_png_e_webp_pela_assinatura`, `Recusa_executavel_disfarcado_de_jpg`, `Recusa_extensao_nao_permitida`, `Recusa_acima_de_2mb` | SegurancaTests |
| RF18 status pelo admin | `Admin_avanca_status_ate_entregue_com_rastreio`, `Nao_envia_pedido_sem_pagamento`, `Envio_exige_codigo_de_rastreio`, `Transicoes_proibidas` | PedidoServiceTests, PedidoTests |
| RN01/RN05 preço congelado e total no servidor | `Total_e_calculado_no_dominio`, `Desconto_nunca_deixa_total_negativo`, `Cartao_aprovado_na_hora` (valor enviado = total do pedido) | PedidoTests, PagamentoServiceTests |
| RN02/RN03 estoque | `Webhook_aprovado_confirma_pedido_e_baixa_estoque`, `Reserva_atomica_nunca_passa_do_estoque`, `Segundo_pagamento_aprovado_do_mesmo_pedido_e_estornado` | PagamentoServiceTests, CheckoutServiceTests |
| RN04 expiração | `Pedido_expirado_sem_pagamento_e_cancelado_e_libera_reserva`, `Pix_pendente_recebe_tolerancia_antes_de_cancelar`, `Antes_de_expirar_consulta_o_gateway_e_confirma_pix_pago_sem_webhook`, `Pagamento_aprovado_depois_do_cancelamento_e_estornado` | PagamentoServiceTests |
| RN07 fluxo de status | `Fluxo_completo_de_status`, `Analise_reprovada_volta_a_aguardar_pagamento`, `Transicoes_proibidas` | PedidoTests |
| RNF05 MFA do admin | `Administrador_exige_segundo_fator`, `Calcula_conforme_rfc6238`, `Aceita_janela_anterior_e_recusa_codigo_antigo`, `Base32_ida_e_volta` | ContaServiceTests, TotpTests |
| RNF07 LGPD | `Cpf_mascarado_esconde_inicio_e_fim`, `Exclusao_lgpd_anonimiza_e_preserva_pedidos`, `Exclusao_bloqueada_com_pedido_em_andamento` | DocumentosTests, ContaServiceTests |
| RNF08 auditoria | `Admin_avanca_status_ate_entregue_com_rastreio` (autor no histórico), `Fluxo_completo_de_status` | PedidoServiceTests, PedidoTests |

## 3. Testes de segurança

| Ataque | Defesa | Como é testado |
|---|---|---|
| **IDOR** (ver/pagar/cancelar pedido de outro) | Toda consulta filtra por `UsuarioId` | `Cliente_nao_ve_pedido_de_outro_cliente`, `Pagamento_de_outro_cliente_e_negado`, `Nao_usa_endereco_de_outro_cliente` |
| **Manipulação de preço/frete** no navegador | Total, frete e parcelas recalculados no servidor | `Recusa_opcao_de_frete_inexistente`, `Cartao_aprovado_na_hora`, `Parcelas_respeitam_a_parcela_minima` |
| **Webhook falso** (marcar pedido como pago) | HMAC-SHA256 + consulta à API antes de mudar status | `Recusa_assinatura_adulterada_ou_ausente`, `Webhook_com_assinatura_invalida_e_registrado_mas_ignorado` |
| **Força bruta** no login e no TOTP | Bloqueio após 5 erros + rate limit 10/min por IP | `Bloqueia_por_15_minutos_apos_5_erros`; rate limit no roteiro S04 |
| **Enumeração de e-mails** | Mesma resposta e mesmo custo de hash | `Email_inexistente_tem_a_mesma_resposta_de_senha_errada`, `Email_ja_cadastrado_responde_igual_e_avisa_o_dono` |
| **Sessão roubada após troca de senha** | Carimbo de segurança | `Redefinicao_de_senha_troca_o_carimbo_e_o_link_vale_uma_vez` |
| **Upload malicioso** | Assinatura do arquivo, extensão, 2 MB, nome gerado, pasta fora do site | Testes de `ValidadorImagem` |
| **CSRF** | `AutoValidateAntiforgeryToken` global | Roteiro S01 |
| **XSS** | Razor codifica toda saída; nenhum `Html.Raw` com dado do usuário; CSP | Roteiro S02 |
| **Injeção SQL** | EF Core parametrizado; nenhum SQL concatenado | Roteiro S03 + revisão (`grep -r "FromSqlRaw\|ExecuteSqlRaw" src` deve voltar vazio) |
| **Pagamento duplicado** | Idempotência + estorno automático do excedente | `Duplo_clique_cria_um_unico_pedido`, `Segundo_pagamento_aprovado_do_mesmo_pedido_e_estornado` |

## 4. Roteiro manual em homologação (ponta a ponta)

Pré-requisitos: credenciais de **teste** do Mercado Pago (`TEST-...`), token do **sandbox** do Melhor Envio, banco com `S001__dados_exemplo.sql`, administrador criado com `criar-admin`.

| # | Cenário | Resultado esperado |
|---|---|---|
| M01 | Abrir a vitrine, paginar e filtrar por categoria e preço | 12 por página; filtros combinados; produto com estoque 0 aparece "Esgotado" |
| M02 | Abrir um produto inativo pela URL | Página 404 |
| M03 | Buscar "cafe" e "CAFETEIRA" | Encontra "Cafeteira Elétrica" (sem diferenciar acento/maiúscula) |
| M04 | Adicionar ao carrinho sem login, depois entrar | Itens continuam no carrinho |
| M05 | Cadastrar com CPF inválido; depois válido; confirmar pelo e-mail | Mensagem "CPF inválido"; e-mail chega; após confirmar, consegue finalizar |
| M06 | Novo endereço digitando o CEP | Rua, bairro, cidade e UF preenchidos |
| M07 | Checkout: trocar de endereço | Opções de frete recotadas (PAC, SEDEX, Jadlog…) com prazo + dias de manuseio |
| M08 | Pagar com **Pix** (sandbox) e aprovar pelo painel de testes do Mercado Pago | Página atualiza sozinha para "Pago"; e-mail "Pagamento confirmado"; estoque baixa |
| M09 | Pagar com **cartão de teste aprovado** (APRO) em 3x | Aprovado na hora; detalhe mostra bandeira, final e parcelas |
| M10 | Cartão de teste recusado (titular "OTHE" ou "FUND") | Mensagem clara; pedido continua aguardando; pode trocar para Pix |
| M11 | Gerar **boleto** | Linha digitável e PDF; vencimento em 3 dias úteis |
| M12 | Gerar Pix e não pagar; aguardar 45 min | Pedido cancelado; reserva liberada; e-mail de cancelamento |
| M13 | Admin: login → configurar autenticador → código | Painel só abre após o código |
| M14 | Admin: pedido pago → Em separação → Enviado (com rastreio) → Entregue | Cliente recebe e-mail a cada etapa; histórico registra o admin |
| M15 | Cliente cancela pedido pago | Estorno aparece no painel do Mercado Pago; estoque volta |
| M16 | Admin: cadastrar produto sem peso | Cadastro recusado |
| M17 | Derrubar o Melhor Envio (token inválido) e cotar | Usa a tabela de contingência; aviso no log |
| M18 | Minha conta → baixar meus dados; excluir conta | JSON com dados; após excluir, login não funciona e pedidos continuam no admin |

## 5. Roteiro de segurança manual

| # | Teste | Esperado |
|---|---|---|
| S01 | Reenviar um POST (ex.: `/pedidos/1/cancelar`) sem o campo `__RequestVerificationToken` (Burp/curl) | 400 |
| S02 | Cadastrar nome `<script>alert(1)</script>` e endereço com `"><img src=x onerror=alert(1)>` | Texto aparece literal, sem executar, na conta, no checkout e no admin |
| S03 | Buscar `' OR 1=1 --` e `%'; DROP TABLE Produto; --` | Busca normal sem resultados; nenhum erro |
| S04 | 15 POSTs em `/conta/entrar` em 1 minuto do mesmo IP | A partir do 11º: 429 |
| S05 | Logado como cliente A, abrir `/pedidos/{id de B}` e `/minha-conta/enderecos/{id de B}` | 404 |
| S06 | Cliente acessar `/admin` | Acesso negado |
| S07 | Admin com senha certa e sem código, tentar `/admin/pedidos` direto | Redireciona; sem acesso |
| S08 | POST em `/webhooks/mercadopago` com `x-signature` inventado | 401; nada muda no pedido; registro em `NotificacaoGateway` |
| S09 | Upload de `.exe` renomeado para `.jpg` e de SVG | Recusados |
| S10 | Cabeçalhos (securityheaders.com ou `curl -I`) | CSP, HSTS, X-Content-Type-Options, X-Frame-Options, Referrer-Policy; sem `Server` e `X-Powered-By` |
| S11 | Trocar a senha em um navegador e usar outro já logado | Sessão do outro cai em até 5 minutos |

## 6. Desempenho (RNF01)

Script k6 sugerido para homologação (100 usuários por 5 minutos na vitrine, busca e produto):

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';
export const options = { vus: 100, duration: '5m', thresholds: { http_req_duration: ['p(95)<1000'] } };
const base = __ENV.URL || 'https://homolog.sualoja.com.br';
export default function () {
  check(http.get(`${base}/`), { 'vitrine 200': (r) => r.status === 200 });
  check(http.get(`${base}/busca?q=fone`), { 'busca 200': (r) => r.status === 200 });
  check(http.get(`${base}/produto/fone-de-ouvido-bluetooth`), { 'produto 200': (r) => r.status === 200 });
  sleep(1);
}
```

Meta: p95 < 1 s. Se não atingir: verificar o plano de execução da vitrine (deve usar `IX_Produto_Vitrine` / `IX_Produto_Ativo_CriadoEm`), habilitar cache de saída (`OutputCache`) na vitrine e servir imagens com CDN.

## 7. Registro de bugs

| Campo | Conteúdo |
|---|---|
| Título | O que falhou, em uma frase |
| Severidade | **Crítico** (perda de dinheiro, dados ou segurança) · **Alto** (fluxo de compra bloqueado) · **Médio** (contorno existe) · **Baixo** (cosmético) |
| Passos | Numerados, com dados usados |
| Esperado × obtido | |
| Evidência | Print, trecho do log com o `RequestId` |

Bugs críticos e altos bloqueiam a implantação.
