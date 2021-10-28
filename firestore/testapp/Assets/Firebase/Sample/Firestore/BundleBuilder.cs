using System.Collections.Generic;
using System.Text;

namespace Firebase.Sample.Firestore {
  public class BundleBuilder {
    private static List<string> BundleTemplate() {
      string metadata =
          @"{
    ""metadata"":{
       ""id"":""test-bundle"",
       ""createTime"":{
         ""seconds"":1001,
         ""nanos"":9999
       },
       ""version"":1,
       ""totalDocuments"":2,
       ""totalBytes"":{totalBytes}
     }
   }";
      string namedQuery1 =
          @"{
    ""namedQuery"":{
       ""name"":""limit"",
       ""readTime"":{
         ""seconds"":1000,
         ""nanos"":9999
       },
       ""bundledQuery"":{
         ""parent"":""projects/{projectId}/databases/(default)/documents"",
         ""structuredQuery"":{
           ""from"":[
           {
             ""collectionId"":""coll-1""
           }
           ],
           ""orderBy"":[
           {
             ""field"":{
               ""fieldPath"":""bar""
             },
             ""direction"":""DESCENDING""
           },
           {
             ""field"":{
               ""fieldPath"":""__name__""
             },
             ""direction"":""DESCENDING""
           }
           ],
           ""limit"":{
             ""value"":1
           }
         },
         ""limitType"":""FIRST""
       }
     }
   }";
      string namedQuery2 =
          @"{
    ""namedQuery"":{
       ""name"":""limit-to-last"",
       ""readTime"":{
         ""seconds"":1000,
         ""nanos"":9999
       },
       ""bundledQuery"":{
         ""parent"":""projects/{projectId}/databases/(default)/documents"",
         ""structuredQuery"":{
           ""from"":[
           {
             ""collectionId"":""coll-1""
           }
           ],
           ""orderBy"":[
           {
             ""field"":{
               ""fieldPath"":""bar""
             },
             ""direction"":""DESCENDING""
           },
           {
             ""field"":{
               ""fieldPath"":""__name__""
             },
             ""direction"":""DESCENDING""
           }
           ],
           ""limit"":{
             ""value"":1
           }
         },
         ""limitType"":""LAST""
       }
     }
   }";
      string documentMetadata1 =
          @"{
       ""documentMetadata"":{
         ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/a"",
         ""readTime"":{
           ""seconds"":1000,
           ""nanos"":9999
         },
         ""exists"":true
       }
     }";

      string document1 =
          @"{
    ""document"":{
       ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/a"",
       ""createTime"":{
         ""seconds"":1,
         ""nanos"":9
       },
       ""updateTime"":{
         ""seconds"":1,
         ""nanos"":9
       },
       ""fields"":{
         ""k"":{
           ""stringValue"":""a""
         },
         ""bar"":{
           ""integerValue"":1
         }
       }
     }
   }";

      string documentMetadata2 =
          @"{
    ""documentMetadata"":{
       ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/b"",
       ""readTime"":{
         ""seconds"":1000,
         ""nanos"":9999
       },
       ""exists"":true
     }
   }";

      string document2 =
          @"{
    ""document"":{
       ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/b"",
       ""createTime"":{
         ""seconds"":1,
         ""nanos"":9
       },
       ""updateTime"":{
         ""seconds"":1,
         ""nanos"":9
       },
       ""fields"":{
         ""k"":{
           ""stringValue"":""b""
         },
         ""bar"":{
           ""integerValue"":2
         }
       }
     }
   }";

      return new List<string> { metadata,  namedQuery1,       namedQuery2, documentMetadata1,
                                document1, documentMetadata2, document2 };
    }

    public static string CreateBundle(string projectId) {
      StringBuilder stringBuilder = new StringBuilder();

      var template = BundleTemplate();
      for (int i = 1; i < template.Count; ++i) {
        string element = template [i].Replace("{projectId}", projectId);
        stringBuilder.Append(Encoding.UTF8.GetBytes(element).Length);
        stringBuilder.Append(element);
      }

      string content = stringBuilder.ToString();
      string metadata =
          template [0].Replace("{totalBytes}", Encoding.UTF8.GetBytes(content).Length.ToString());
      return Encoding.UTF8.GetBytes(metadata).Length.ToString() + metadata + content;
    }
  }
}
