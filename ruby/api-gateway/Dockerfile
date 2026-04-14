FROM ruby:3.2-slim

WORKDIR /app
COPY Gemfile ./
RUN bundle install --without development test
COPY app.rb ./

ENV PORT=8080
EXPOSE 8080
CMD ["ruby", "app.rb"]
