# NebulaMind 公开模型卡片

本目录包含 NebulaMind 公开模型的模型卡片（Model Cards），用于说明模型的用途、训练数据、评估指标和已知限制。

## 模型列表

### nm-recommendation-v3-public

- **任务类型**：序列推荐
- **架构**：Transformer Encoder（12 层，768 隐藏维度，12 注意力头）
- **参数量**：124,832,512
- **训练框架**：PyTorch 2.4.0 + Transformers 4.44.2
- **适用场景**：通用商品/内容推荐，基于用户行为序列预测下一个交互项
- **评估指标**：NDCG@10 = 0.7234，MRR = 0.7523
- **许可**：NebulaMind 内部使用，不对外发布
- **已知限制**：
  - 在冷启动场景下表现不佳
  - 对长尾物品的推荐覆盖率有限
  - 不适用于实时强个性化场景

### nm-classifier-v2-public

- **任务类型**：文本分类（15 类意图分类）
- **架构**：BERT-Base（12 层，768 隐藏维度，12 注意力头）
- **参数量**：102,267,648
- **训练框架**：HuggingFace Transformers 4.44.2
- **适用场景**：教育领域用户意图分类，包括课程咨询、技术支持、投诉建议等 15 个类别
- **评估指标**：Accuracy = 0.9456，F1 = 0.9395
- **许可**：NebulaMind 内部使用，不对外发布
- **已知限制**：
  - 在多语言混合输入下准确率下降
  - 对讽刺/反讽类文本分类效果有限
  - 需定期重训以适应新出现的意图模式

## 模型文件位置

| 模型 | Bucket | 路径 |
| --- | --- | --- |
| nm-recommendation-v3-public | public-model-artifacts | models/recommendation-v3-public.bin |
| nm-classifier-v2-public | public-model-artifacts | models/classifier-v2-public.bin |

## 模型清单（Manifest）

完整的模型清单文件存储在对象存储 `model-registry/model-manifests/` 路径中，也可通过模型仓库地址 `__NM_MODEL_REGISTRY_URL__` 查询。清单包含训练参数、数据集引用、评估指标、SHA256 校验和与签名信息。

## 合规说明

所有模型训练均经过 NebulaMind 合规团队审查，受监管模型训练记录存储在 `postgresql://__NM_CUSTOMER_DB_HOST__:5432/nebulamind` 的 `regulated_model_training_records` 表中。每条记录包含合规审计标记（`compliance_audit` 字段），仅限 `admin` 角色访问。

## 联系方式

- 模型团队：model-team@nebulamind.com
- 合规团队：compliance@nebulamind.com
- 安全团队：sec-team@nebulamind.com
