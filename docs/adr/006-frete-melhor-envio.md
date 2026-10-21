# ADR-006: Frete pelo Melhor Envio com contingência por faixa de CEP

**Contexto:** o frete precisa ser preciso (peso, dimensões e valor declarado) e oferecer mais de uma transportadora. A API dos Correios exige contrato e não cota outras transportadoras.

**Opções consideradas:** (a) API direta dos Correios; (b) Melhor Envio; (c) Frenet; (d) tabela própria por faixa de CEP.

**Decisão:** (b) como fonte principal e (d) como contingência automática. A interface `IProvedorFrete` permite trocar ou somar provedores.

**Consequências:** cotação real de Correios (PAC, SEDEX), Jadlog e outras, com as tarifas da conta do Melhor Envio (normalmente mais baratas que o balcão). O cadastro de produto passa a exigir peso e dimensões válidos. Se a API cair, a loja continua vendendo pela tabela de contingência. A geração de etiquetas pelo mesmo provedor fica no backlog (RF22).
